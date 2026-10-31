namespace Genesis.Engine.Core;

/// <summary>Deterministic four-neighbor A*; empty means blocked or the search budget was exhausted.</summary>
public static class GridPathfinder
{
    public static IReadOnlyList<Vec2> Find(TerrainMap terrain, Vec2 start, Vec2 end, Func<int, int, bool>? blocked = null, int budget = 10000)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        Guard.Position(start, nameof(start)); Guard.Position(end, nameof(end));
        if (budget <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
        bool Inside(Vec2 p) => p.X >= 0 && p.Y >= 0 && p.X < terrain.Width && p.Y < terrain.Height;
        if (!Inside(start) || !Inside(end)) return Array.Empty<Vec2>();
        bool Passable(int x, int y) => terrain.Get(x,y) >= -.15f && terrain.Get(x,y) < 1.5f && !(blocked?.Invoke(x,y) ?? false);
        int sx=(int)start.X, sy=(int)start.Y, tx=(int)end.X, ty=(int)end.Y;
        if (!Passable(sx,sy) || !Passable(tx,ty)) return Array.Empty<Vec2>();
        int source=sy*terrain.Width+sx, target=ty*terrain.Width+tx;
        var costs=new Dictionary<int,float>{{source,0}};
        var previous=new Dictionary<int,int>();
        var open=new PriorityQueue<int,(float Score,int Order)>();
        int order=0;
        open.Enqueue(source,(Math.Abs(tx-sx)+Math.Abs(ty-sy),order++));
        var closed=new HashSet<int>();
        (int X,int Y)[] directions=[(1,0),(0,1),(-1,0),(0,-1)];
        while(open.Count>0 && closed.Count<budget)
        {
            int current=open.Dequeue();
            if(!closed.Add(current)) continue;
            if(current==target)
            {
                var path=new List<Vec2>();
                for(int node=target;;node=previous[node])
                {
                    path.Add(new(node%terrain.Width,node/terrain.Width));
                    if(node==source) break;
                }
                path.Reverse(); return path;
            }
            int x=current%terrain.Width,y=current/terrain.Width;
            foreach(var d in directions)
            {
                int nx=x+d.X,ny=y+d.Y;
                if(nx<0||ny<0||nx>=terrain.Width||ny>=terrain.Height||!Passable(nx,ny)) continue;
                int next=ny*terrain.Width+nx;
                if(closed.Contains(next)) continue;
                float cost=costs[current]+1+Math.Abs(terrain.Get(nx,ny)-terrain.Get(x,y));
                if(costs.TryGetValue(next,out float old)&&old<=cost) continue;
                costs[next]=cost;previous[next]=current;
                open.Enqueue(next,(cost+Math.Abs(tx-nx)+Math.Abs(ty-ny),order++));
            }
        }
        return Array.Empty<Vec2>();
    }
}

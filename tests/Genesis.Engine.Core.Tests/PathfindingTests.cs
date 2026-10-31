using Genesis.Engine.Core;
namespace Genesis.Engine.Core.Tests;

public class PathfindingTests
{
    [Fact]
    public void RoutesAroundMountainWallThroughGap()
    {
        var map = new TerrainMap(9,7);
        for(int y=0;y<6;y++) map.Set(4,y,3);
        var path = GridPathfinder.Find(map,new(1,2),new(7,2));
        Assert.NotEmpty(path);
        Assert.Contains(new Vec2(4,6),path);
        Assert.All(path,p=>Assert.True(map.Get((int)p.X,(int)p.Y)<1.5f));
        for(int i=1;i<path.Count;i++) Assert.Equal(1,Math.Abs(path[i].X-path[i-1].X)+Math.Abs(path[i].Y-path[i-1].Y));
        Assert.Equal(path,GridPathfinder.Find(map,new(1,2),new(7,2)));
    }
    [Fact]
    public void SettlementPaysForRoadSegmentAroundObstacle()
    {
        var sim = new Simulation(12,12);
        var data = System.Text.Json.JsonSerializer.Deserialize<WorldSave>(sim.SaveJson())!;
        Array.Fill(data.Terrain, 0);
        for(int y=0;y<7;y++) data.Terrain[y*12+5]=3;
        var traits = new float[]{1,1,2,1,.1f,1,.5f};
        data.Agents = Enumerable.Range(0,3).Select(i=>new AgentSave
        {
            Id=i,Position=new(2+i,5),Energy=1,Age=10,Traits=traits,
            Brain=new NeuralNetwork(new float[2,Agent.SensorCount]).ToSnapshot()
        }).ToArray();
        data.Tribes=[new TribeSave{Id=0,Center=new(3,5),Members=[0,1,2],Wood=10,Stone=10,Food=1,HasWeapons=true,HasFarming=true}];
        data.Structures=[new(StructureKind.Hut,new(3,5),0),new(StructureKind.Farm,new(8,5),0)];
        sim=Simulation.LoadJson(System.Text.Json.JsonSerializer.Serialize(data));
        sim.Step(1);
        var road=Assert.Single(sim.Structures.Where(s=>s.Kind==StructureKind.Road));
        Assert.True(sim.Terrain.Get((int)road.Position.X,(int)road.Position.Y)<1.5f);
        Assert.True(sim.Tribes[0].Stone<10+.015f*3); // gathered resources minus paid segment
        Assert.True(sim.Tribes[0].Wood<10+.025f*3, $"Wood={sim.Tribes[0].Wood}, members={sim.Tribes[0].MemberIds.Count}, agents={sim.Agents.Count}");
    }

    [Fact]
    public void WaterFireAndBudgetCanBlockPaths()
    {
        var map = new TerrainMap(5,5);
        for(int y=0;y<5;y++) map.Set(2,y,-1);
        Assert.Empty(GridPathfinder.Find(map,new(0,2),new(4,2)));
        Assert.Empty(GridPathfinder.Find(map,new(0,0),new(1,4),budget:1));
        Assert.Empty(GridPathfinder.Find(map,new(0,0),new(1,1),(x,y)=>y==1));
        Assert.Empty(GridPathfinder.Find(map,new(-1,0),new(1,1)));
    }
}

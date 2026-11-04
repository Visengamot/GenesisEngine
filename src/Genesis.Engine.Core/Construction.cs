namespace Genesis.Engine.Core;

public sealed class ConstructionSite
{
    public int TribeId { get; set; }
    public StructureKind Kind { get; set; }
    public Vec2 Position { get; set; }
    public float PaidWood { get; set; }
    public float PaidStone { get; set; }
    public float Labor { get; set; }
    public int? WorkerId { get; set; }
    public Vec2[] Route { get; set; } = [];
    public int Waypoint { get; set; }
    public long RetryTick { get; set; }
    public string BlockReason { get; set; } = "";
    public float RequiredLabor => Kind == StructureKind.Hut ? 4 : 6;
}

public sealed partial class Simulation
{
    private readonly List<ConstructionSite> constructionSites = new();
    public IReadOnlyList<ConstructionSite> ConstructionSites => constructionSites.AsReadOnly();

    private bool TryConstructionMotion(Agent worker, float dt, out Vec2 motion)
    {
        motion = default;
        var site = constructionSites.FirstOrDefault(s => s.WorkerId == worker.Id);
        if (site is null || worker.Energy < .35f) return false;
        if (site.BlockReason.Length > 0) return true;
        while (site.Waypoint < site.Route.Length && DistanceSquared(worker.Position, site.Route[site.Waypoint]) < .04f) site.Waypoint++;
        if (site.Waypoint >= site.Route.Length) return true;
        var next = site.Route[site.Waypoint];
        if (!HaulPassable((int)next.X, (int)next.Y))
        { site.BlockReason = "route-blocked"; site.RetryTick = Tick + 30; return true; }
        var delta = new Vec2(next.X-worker.Position.X, next.Y-worker.Position.Y);
        float length = MathF.Sqrt(delta.X*delta.X+delta.Y*delta.Y);
        motion = delta * (1 / Math.Max(length, worker.Genome.Speed*4*dt));
        return true;
    }

    private void StepConstruction(float dt)
    {
        for (int i = constructionSites.Count - 1; i >= 0; i--)
        {
            var site = constructionSites[i];
            var tribe = tribes.FirstOrDefault(t => t.Id == site.TribeId);
            if (tribe is null) { constructionSites.RemoveAt(i); continue; } // Invested materials are abandoned.
            var worker = Agents.FirstOrDefault(a => a.Id == site.WorkerId && a.Health > 0 && a.Energy >= .35f && tribe.MemberIds.Contains(a.Id));
            if (worker is null)
            {
                site.WorkerId = null; site.Route = []; site.Waypoint = 0;
                if (Tick < site.RetryTick) continue;
                worker = Agents.Where(a => tribe.MemberIds.Contains(a.Id) && a.Health > 0 && a.Energy > .8f &&
                    !haulJobs.Any(j => j.WorkerId == a.Id) && !constructionSites.Any(s => s.WorkerId == a.Id))
                    .OrderBy(a => DistanceSquared(a.Position, site.Position)).ThenBy(a => a.Id).FirstOrDefault();
                if (worker is null) { site.BlockReason = "no-worker"; site.RetryTick = Tick+30; continue; }
                site.WorkerId = worker.Id; site.BlockReason = "route-needed";
            }
            if (site.BlockReason.Length > 0)
            {
                if (Tick < site.RetryTick) continue;
                var path = GridPathfinder.Search(Terrain, worker.Position, site.Position, (x,y)=>!HaulPassable(x,y), 512);
                if (path.Status != PathSearchStatus.Found)
                {
                    site.WorkerId = null; site.BlockReason = path.Status.ToString(); site.RetryTick = Tick+90;
                    continue;
                }
                site.Route = path.Path.ToArray(); site.Waypoint = 0; site.BlockReason = "";
            }
            if (DistanceSquared(worker.Position, site.Position) >= .16f) continue;
            if (!HaulPassable((int)site.Position.X,(int)site.Position.Y))
            { site.BlockReason = "site-blocked"; site.RetryTick = Tick+30; continue; }
            site.Labor = Math.Min(site.RequiredLabor, site.Labor+dt);
            worker.Energy = Math.Max(0, worker.Energy-dt*.025f);
            if (site.Labor < site.RequiredLabor) continue;
            structures.Add(new(site.Kind, site.Position, site.TribeId));
            RecordEvent($"Племя {tribe.Id} завершило {(site.Kind == StructureKind.Hut ? "хижину" : "ферму")}.");
            constructionSites.RemoveAt(i);
        }
    }

    private void RestoreConstruction(WorldSave save)
    {
        if (save.Version < 13) return;
        if (save.ConstructionSites is null || save.ConstructionSites.Length > 64 || save.ConstructionSites.Length+structures.Count > 128)
            throw new ArgumentException("Invalid construction count.");
        var occupied = structures.Select(s=>s.Position).ToHashSet();
        var workers = haulJobs.Select(j=>j.WorkerId).ToHashSet();
        var kinds = new HashSet<(int,StructureKind)>();
        foreach (var s in save.ConstructionSites)
        {
            if (s is null || (s.Kind is not (StructureKind.Hut or StructureKind.Farm)) || !tribes.Any(t=>t.Id==s.TribeId) ||
                !occupied.Add(s.Position) || !kinds.Add((s.TribeId,s.Kind)) || structures.Any(b=>b.TribeId==s.TribeId&&b.Kind==s.Kind) ||
                !float.IsFinite(s.Labor) || s.Labor < 0 || s.Labor >= s.RequiredLabor ||
                !float.IsFinite(s.PaidWood) || s.PaidWood != (s.Kind==StructureKind.Hut?.6f:.5f) ||
                !float.IsFinite(s.PaidStone) || s.PaidStone != (s.Kind==StructureKind.Hut?.2f:.1f) ||
                s.RetryTick < 0 || s.Route is null || s.Route.Length > 513 || s.Waypoint < 0 || s.Waypoint>s.Route.Length ||
                s.BlockReason is null || s.BlockReason.Length>64 ||
                (s.WorkerId is int id && (!workers.Add(id)||!Agents.Any(a=>a.Id==id)||!tribes.Any(t=>t.Id==s.TribeId&&t.MemberIds.Contains(id)))))
                throw new ArgumentException("Invalid construction site.");
            foreach(var p in s.Route.Append(s.Position))
            { Guard.Position(p,nameof(save)); if(p!=Clamp(p)||p.X!=(int)p.X||p.Y!=(int)p.Y)throw new ArgumentException("Invalid construction route."); }
            for(int i=1;i<s.Route.Length;i++)
                if(Math.Abs(s.Route[i].X-s.Route[i-1].X)+Math.Abs(s.Route[i].Y-s.Route[i-1].Y)!=1)throw new ArgumentException("Invalid construction adjacency.");
            if(s.Route.Length>0&&s.Route[^1]!=s.Position)throw new ArgumentException("Wrong construction destination.");
            constructionSites.Add(s);
        }
    }
}

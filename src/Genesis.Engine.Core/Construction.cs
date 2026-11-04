namespace Genesis.Engine.Core;

public sealed class ConstructionSite
{
    public int TribeId { get; set; }
    public StructureKind Kind { get; set; }
    public Vec2 Position { get; set; }
    public float PaidWood { get; set; }
    public float PaidStone { get; set; }
    public float Labor { get; set; }
    public bool MaterialsDelivered { get; set; } = true;
    public Vec2 MaterialPosition { get; set; }
    public bool MaterialsInTransit { get; set; }
    public Vec2 MaterialOrigin { get; set; }
    public float DeliveredMaterialAmount { get; set; }
    public float CargoMaterialAmount { get; set; }
    public float TotalMaterialAmount => PaidWood + PaidStone;
    // Canonical wood-first packing: amounts encode an ordered prefix of the paid recipe.
    // Derived typed quantities cannot drift apart from the compact v20 save representation.
    [System.Text.Json.Serialization.JsonIgnore]
    public float DeliveredWood => Math.Min(PaidWood, DeliveredMaterialAmount);
    [System.Text.Json.Serialization.JsonIgnore]
    public float DeliveredStone => Math.Min(PaidStone, Math.Max(0, DeliveredMaterialAmount - PaidWood));
    [System.Text.Json.Serialization.JsonIgnore]
    public float CargoWood => Math.Min(Math.Max(0, PaidWood - DeliveredWood), CargoMaterialAmount);
    [System.Text.Json.Serialization.JsonIgnore]
    public float CargoStone => Math.Min(Math.Max(0, PaidStone - DeliveredStone), Math.Max(0, CargoMaterialAmount - CargoWood));
    [System.Text.Json.Serialization.JsonIgnore]
    public float UncollectedWood => Math.Max(0, PaidWood - DeliveredWood - CargoWood);
    [System.Text.Json.Serialization.JsonIgnore]
    public float UncollectedStone => Math.Max(0, PaidStone - DeliveredStone - CargoStone);
    public Vec2 Destination => MaterialsDelivered || MaterialsInTransit ? Position : MaterialPosition;
    public int? WorkerId { get; set; }
    public Vec2[] Route { get; set; } = [];
    public int Waypoint { get; set; }
    public long RetryTick { get; set; }
    public long AssignmentDeadlineTick { get; set; }
    public string BlockReason { get; set; } = "";
    public float RequiredLabor => Kind == StructureKind.Hut ? 4 : 6;
}

public sealed partial class Simulation
{
    private readonly List<ConstructionSite> constructionSites = new();
    public IReadOnlyList<ConstructionSite> ConstructionSites => constructionSites.AsReadOnly();
    public float LostConstructionWood { get; private set; }
    public float LostConstructionStone { get; private set; }

    private void AbandonConstruction(int index, string reason)
    {
        var site = constructionSites[index];
        LostConstructionWood += site.PaidWood;
        LostConstructionStone += site.PaidStone;
        constructionSites.RemoveAt(index);
        RecordEvent($"Племя {site.TribeId}: стройка потеряна ({reason}), дерево {site.PaidWood:0.00}, камень {site.PaidStone:0.00}.");
    }

    private bool TryConstructionMotion(Agent worker, float dt, out Vec2 motion)
    {
        motion = default;
        ConstructionSite? site = null;
        foreach (var candidate in constructionSites)
            if (candidate.WorkerId == worker.Id) { site = candidate; break; }
        if (site is null || worker.Energy < .35f || Tick >= site.AssignmentDeadlineTick) return false;
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
            if (tribe is null) { AbandonConstruction(i, "нет жителей"); continue; }
            if (site.WorkerId is not null && Tick >= site.AssignmentDeadlineTick)
            {
                var former = Agents.FirstOrDefault(a => a.Id == site.WorkerId);
                if (site.MaterialsInTransit)
                {
                    if (former is not null) site.MaterialPosition = new((int)former.Position.X, (int)former.Position.Y);
                    site.MaterialsInTransit = false;
                }
                site.WorkerId = null; site.Route = []; site.Waypoint = 0;
                site.AssignmentDeadlineTick = 0; site.RetryTick = Tick + 90; site.BlockReason = "assignment-expired";
                RecordEvent($"Племя {site.TribeId}: строитель освобождён по сроку, стройка сохранена.");
                continue;
            }
            var worker = Agents.FirstOrDefault(a => a.Id == site.WorkerId && a.Health > 0 && a.Energy >= .35f && tribe.MemberIds.Contains(a.Id));
            if (worker is null)
            {
                if (site.MaterialsInTransit)
                {
                    // Drop the paid bundle where the former carrier last stood; replacement must recover it.
                    var former = Agents.FirstOrDefault(a => a.Id == site.WorkerId);
                    if (former is not null) site.MaterialPosition = new((int)former.Position.X, (int)former.Position.Y);
                    site.MaterialsInTransit = false;
                }
                site.WorkerId = null; site.Route = []; site.Waypoint = 0; site.AssignmentDeadlineTick = 0;
                if (Tick < site.RetryTick) continue;
                var candidates = Agents.Where(a => tribe.MemberIds.Contains(a.Id) && a.Health > 0 && a.Energy > .8f &&
                    !haulJobs.Any(j => j.WorkerId == a.Id) && !constructionSites.Any(s => s.WorkerId == a.Id))
                    .OrderBy(a => DistanceSquared(a.Position, site.Destination)).ThenBy(a => a.Id).Take(4).ToArray();
                string reason = "no-worker";
                foreach (var candidate in candidates)
                {
                    var path = GridPathfinder.Search(Terrain, candidate.Position, site.Destination, (x,y)=>!HaulPassable(x,y), 512);
                    if (path.Status != PathSearchStatus.Found) { reason = path.Status.ToString(); continue; }
                    worker = candidate; site.Route = path.Path.ToArray(); site.Waypoint = 0;
                    break;
                }
                if (worker is null) { site.BlockReason = reason; site.RetryTick = Tick+90; continue; }
                site.WorkerId = worker.Id; site.AssignmentDeadlineTick = Tick + 1800; site.BlockReason = "";
            }
            if (site.BlockReason.Length > 0)
            {
                if (Tick < site.RetryTick) continue;
                var path = GridPathfinder.Search(Terrain, worker.Position, site.Destination, (x,y)=>!HaulPassable(x,y), 512);
                if (path.Status != PathSearchStatus.Found)
                {
                    if (site.MaterialsInTransit)
                    { site.MaterialPosition = new((int)worker.Position.X, (int)worker.Position.Y); site.MaterialsInTransit = false; }
                    site.Route = []; site.Waypoint = 0;
                    site.WorkerId = null; site.AssignmentDeadlineTick = 0; site.BlockReason = path.Status.ToString(); site.RetryTick = Tick+90;
                    continue;
                }
                site.Route = path.Path.ToArray(); site.Waypoint = 0; site.BlockReason = "";
            }
            if (site.MaterialsInTransit) site.MaterialPosition = new((int)worker.Position.X, (int)worker.Position.Y);
            if (DistanceSquared(worker.Position, site.Destination) >= .16f) continue;
            if (!site.MaterialsDelivered)
            {
                if (site.MaterialsInTransit)
                {
                    site.DeliveredMaterialAmount = Math.Min(site.TotalMaterialAmount, site.DeliveredMaterialAmount + site.CargoMaterialAmount);
                    site.CargoMaterialAmount = 0; site.MaterialsInTransit = false;
                    site.MaterialsDelivered = site.DeliveredWood >= site.PaidWood - .000001f &&
                        site.DeliveredStone >= site.PaidStone - .000001f;
                    site.MaterialPosition = site.MaterialOrigin;
                }
                else
                {
                    // A dropped partial load is recovered before another load is collected at the origin.
                    if (site.CargoMaterialAmount <= 0)
                        site.CargoMaterialAmount = Math.Min(.25f, site.TotalMaterialAmount - site.DeliveredMaterialAmount);
                    site.MaterialsInTransit = true;
                }
                site.Route = []; site.Waypoint = 0; site.BlockReason = "route-needed"; site.RetryTick = Tick;
                continue;
            }
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
        if (save.Version >= 19)
        {
            if (!float.IsFinite(save.LostConstructionWood) || save.LostConstructionWood < 0 ||
                !float.IsFinite(save.LostConstructionStone) || save.LostConstructionStone < 0)
                throw new ArgumentException("Invalid construction losses.");
            LostConstructionWood = save.LostConstructionWood;
            LostConstructionStone = save.LostConstructionStone;
        }
        if (save.ConstructionSites is null || save.ConstructionSites.Length > 64 || save.ConstructionSites.Length+structures.Count > 128)
            throw new ArgumentException("Invalid construction count.");
        var occupied = structures.Select(s=>s.Position).ToHashSet();
        var workers = haulJobs.Select(j=>j.WorkerId).ToHashSet();
        var kinds = new HashSet<(int,StructureKind)>();
        foreach (var s in save.ConstructionSites)
        {
            if (s is null || (s.Kind is not (StructureKind.Hut or StructureKind.Farm)) || !tribes.Any(t=>t.Id==s.TribeId) ||
                !occupied.Add(s.Position) || !kinds.Add((s.TribeId,s.Kind)) || structures.Count(b=>b.TribeId==s.TribeId&&b.Kind==s.Kind) >= (s.Kind==StructureKind.Hut && save.Version>=16 ? 8 : 1) ||
                !float.IsFinite(s.Labor) || s.Labor < 0 || s.Labor >= s.RequiredLabor ||
                !float.IsFinite(s.PaidWood) || s.PaidWood != (s.Kind==StructureKind.Hut?.6f:.5f) ||
                !float.IsFinite(s.PaidStone) || s.PaidStone != (s.Kind==StructureKind.Hut?.2f:.1f) ||
                s.RetryTick < 0 || s.Route is null || s.Route.Length > 513 || s.Waypoint < 0 || s.Waypoint>s.Route.Length ||
                s.BlockReason is null || s.BlockReason.Length>64 ||
                (s.WorkerId is int id && (!workers.Add(id)||!Agents.Any(a=>a.Id==id)||!tribes.Any(t=>t.Id==s.TribeId&&t.MemberIds.Contains(id)))))
                throw new ArgumentException("Invalid construction site.");
            if (save.Version < 18) s.AssignmentDeadlineTick = s.WorkerId is null ? 0 : Tick + 1800;
            if (s.AssignmentDeadlineTick < 0 || (s.WorkerId is not null && s.AssignmentDeadlineTick == 0) ||
                (s.WorkerId is null && s.AssignmentDeadlineTick != 0)) throw new ArgumentException("Invalid construction assignment deadline.");
            if (save.Version >= 15 && ((s.MaterialsDelivered && s.MaterialsInTransit) || (s.MaterialsInTransit && s.WorkerId is null) ||
                (!s.MaterialsDelivered && s.Labor > 0))) throw new ArgumentException("Invalid construction material state.");
            if (save.Version < 20)
            {
                s.MaterialOrigin = s.MaterialPosition;
                s.DeliveredMaterialAmount = s.MaterialsDelivered ? s.TotalMaterialAmount : 0;
                s.CargoMaterialAmount = 0;
                if (s.MaterialsInTransit)
                {
                    // Legacy saves carried the entire bundle: split it at its recorded location on recovery.
                    s.MaterialsInTransit = false; s.Route = []; s.Waypoint = 0; s.BlockReason = "route-needed";
                }
            }
            if (!float.IsFinite(s.DeliveredMaterialAmount) || !float.IsFinite(s.CargoMaterialAmount) ||
                s.DeliveredMaterialAmount < 0 || s.CargoMaterialAmount < 0 || s.CargoMaterialAmount > .25f ||
                s.DeliveredMaterialAmount + s.CargoMaterialAmount > s.TotalMaterialAmount + .000001f ||
                (s.MaterialsInTransit && s.CargoMaterialAmount <= 0) ||
                s.MaterialsDelivered != (s.DeliveredMaterialAmount >= s.TotalMaterialAmount - .000001f) ||
                (s.MaterialsDelivered && s.CargoMaterialAmount != 0)) throw new ArgumentException("Invalid construction quantities.");
            foreach(var p in s.Route.Append(s.Position).Append(s.MaterialPosition).Append(s.MaterialOrigin))
            { Guard.Position(p,nameof(save)); if(p!=Clamp(p)||p.X!=(int)p.X||p.Y!=(int)p.Y)throw new ArgumentException("Invalid construction route."); }
            for(int i=1;i<s.Route.Length;i++)
                if(Math.Abs(s.Route[i].X-s.Route[i-1].X)+Math.Abs(s.Route[i].Y-s.Route[i-1].Y)!=1)throw new ArgumentException("Invalid construction adjacency.");
            if(s.Route.Length>0&&s.Route[^1]!=s.Destination)throw new ArgumentException("Wrong construction destination.");
            constructionSites.Add(s);
        }
    }
}

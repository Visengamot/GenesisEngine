namespace Genesis.Engine.Core;

public enum HaulResource { Food, Wood, Stone }
public enum HaulStage { Outbound, Returning, Blocked, WaitingForCapacity }
public sealed class HaulJob
{
    public int WorkerId { get; set; }
    public int TribeId { get; set; }
    public HaulResource Resource { get; set; }
    public Vec2 Source { get; set; }
    public Vec2 Home { get; set; }
    public Vec2[] Route { get; set; } = [];
    public int Waypoint { get; set; }
    public HaulStage Stage { get; set; }
    public float Cargo { get; set; }
    public long RetryTick { get; set; }
    public long DeadlineTick { get; set; }
    public string BlockReason { get; set; } = "";
}

public sealed record HaulSourceFailure(int TribeId, HaulResource Resource, Vec2 Source, long UntilTick);

public sealed partial class Simulation
{
    private readonly List<HaulSourceFailure> haulSourceFailures = new();
    public IReadOnlyList<HaulSourceFailure> HaulSourceFailures => haulSourceFailures.AsReadOnly();

    private void RememberHaulFailure(int tribeId, HaulResource resource, Vec2 source)
    {
        haulSourceFailures.RemoveAll(f => f.TribeId == tribeId && f.Resource == resource && f.Source == source);
        if (haulSourceFailures.Count >= 256) haulSourceFailures.RemoveAt(0);
        haulSourceFailures.Add(new(tribeId, resource, source, Tick + 900));
    }
    private readonly List<HaulJob> haulJobs = new();
    public IReadOnlyList<HaulJob> HaulJobs => haulJobs.AsReadOnly();
    /// <summary>Explicit abandoned cargo on death, erased worker, or destroyed home, in normalized units.</summary>
    public float LostHaulCargo { get; private set; }
    public float DeliveredHaulCargo { get; private set; }

    private void RestoreHauling(WorldSave save)
    {
        if (save.Version < 9) return;
        if (save.HaulJobs is null || save.HaulJobs.Length > (save.Version >= 10 ? 128 : 32) || !float.IsFinite(save.LostHaulCargo) || save.LostHaulCargo < 0 ||
            !float.IsFinite(save.DeliveredHaulCargo) || save.DeliveredHaulCargo < 0) throw new ArgumentException("Invalid hauling counters.");
        var workers = new HashSet<int>();
        var sourceClaims = new HashSet<(HaulResource, Vec2)>();
        foreach (var j in save.HaulJobs)
        {
            if (j is null || !workers.Add(j.WorkerId) || !Enum.IsDefined(j.Resource) || !Enum.IsDefined(j.Stage) ||
                !float.IsFinite(j.Cargo) || j.Cargo < 0 || j.Cargo > .25f || j.RetryTick < 0 || (save.Version >= 11 && j.DeadlineTick <= 0) || j.Route is null ||
                j.Route.Length > 513 || j.Waypoint < 0 || j.Waypoint > j.Route.Length || j.BlockReason is null || j.BlockReason.Length > 64 ||
                !Agents.Any(a => a.Id == j.WorkerId) || !tribes.Any(t => t.Id == j.TribeId && t.MemberIds.Contains(j.WorkerId)) ||
                !structures.Any(s => s.TribeId == j.TribeId && s.Kind == StructureKind.Hut && s.Position == j.Home))
                throw new ArgumentException("Invalid hauling job.");
            if (save.Version >= 10 && j.Cargo == 0 && !sourceClaims.Add((j.Resource, j.Source)))
                throw new ArgumentException("Duplicate resource claim.");
            if ((j.Stage == HaulStage.Outbound && j.Cargo != 0) ||
                ((j.Stage is HaulStage.Returning or HaulStage.WaitingForCapacity) && j.Cargo <= 0))
                throw new ArgumentException("Inconsistent hauling stage and cargo.");
            foreach (var p in j.Route.Append(j.Source).Append(j.Home))
            { Guard.Position(p, nameof(save)); if (p != Clamp(p) || p.X != (int)p.X || p.Y != (int)p.Y) throw new ArgumentException("Invalid hauling route."); }
            for (int i = 1; i < j.Route.Length; i++)
                if (Math.Abs(j.Route[i].X-j.Route[i-1].X)+Math.Abs(j.Route[i].Y-j.Route[i-1].Y) != 1)
                    throw new ArgumentException("Nonadjacent hauling route.");
            if (j.Route.Length > 0 && j.Route[^1] != (j.Cargo > 0 ? j.Home : j.Source))
                throw new ArgumentException("Hauling route has wrong destination.");
            if (save.Version < 11) j.DeadlineTick = Tick + 1800;
            haulJobs.Add(j);
        }
        if (save.Version >= 12)
        {
            if (save.HaulSourceFailures is null || save.HaulSourceFailures.Length > 256) throw new ArgumentException("Invalid hauling failure memory.");
            var keys = new HashSet<(int, HaulResource, Vec2)>();
            foreach (var f in save.HaulSourceFailures)
            {
                if (f is null || !Enum.IsDefined(f.Resource) || f.UntilTick < 0 || !tribes.Any(t => t.Id == f.TribeId) ||
                    !keys.Add((f.TribeId, f.Resource, f.Source))) throw new ArgumentException("Invalid hauling failure.");
                Guard.Position(f.Source, nameof(save));
                if (f.Source != Clamp(f.Source) || f.Source.X != (int)f.Source.X || f.Source.Y != (int)f.Source.Y)
                    throw new ArgumentException("Invalid failed source position.");
                haulSourceFailures.Add(f);
            }
        }
        LostHaulCargo = save.LostHaulCargo; DeliveredHaulCargo = save.DeliveredHaulCargo;
    }

    private bool HaulPassable(int x, int y) => Terrain.Get(x, y) >= -.15f && Terrain.Get(x, y) < 1.5f && Environment.Get(x, y).Fire < .05f;
    private float AvailableResource(HaulResource r, Vec2 p) => r switch
    {
        HaulResource.Food => Food.Get((int)p.X, (int)p.Y),
        HaulResource.Wood => Environment.Get((int)p.X, (int)p.Y).Wood,
        _ => Environment.Get((int)p.X, (int)p.Y).Stone
    };

    /// <summary>Stock deficits include committed loads; stable resource order breaks equal scores.</summary>
    public HaulResource MostNeededResource(Tribe tribe) => ResourcePriorities(tribe)[0];

    private HaulResource[] ResourcePriorities(Tribe tribe)
    {
        ArgumentNullException.ThrowIfNull(tribe);
        float Incoming(HaulResource resource) => haulJobs.Where(j => j.TribeId == tribe.Id && j.Resource == resource)
            .Sum(j => j.Cargo > 0 ? j.Cargo : .25f);
        float foodTarget = Math.Max(.5f, tribe.MemberIds.Count * .04f);
        var needs = new[]
        {
            (Resource: HaulResource.Food, Score: (foodTarget - tribe.Food - Incoming(HaulResource.Food)) / foodTarget),
            (Resource: HaulResource.Wood, Score: (2 - tribe.Wood - Incoming(HaulResource.Wood)) / 2),
            (Resource: HaulResource.Stone, Score: (2 - tribe.Stone - Incoming(HaulResource.Stone)) / 2)
        };
        return needs.OrderByDescending(n => n.Score).ThenBy(n => n.Resource).Select(n => n.Resource).ToArray();
    }

    private void AssignHauler(Tribe tribe, Agent[] members, Structure home)
    {
        // At most one new assignment per decision, up to four workers / a quarter of citizens.
        int workerBudget = Math.Clamp(members.Length / 4, 1, 4);
        if (haulJobs.Count >= 128 || haulJobs.Count(j => j.TribeId == tribe.Id) >= workerBudget) return;
        var worker = members.Where(a => a.Health > 0 && a.Energy > .8f && !haulJobs.Any(j => j.WorkerId == a.Id) && !constructionSites.Any(s => s.WorkerId == a.Id))
            .OrderBy(a => DistanceSquared(a.Position, home.Position)).ThenBy(a => a.Id).FirstOrDefault();
        if (worker is null) return;
        int searchBudget = 2048;
        foreach (var resource in ResourcePriorities(tribe))
        {
        // Bounded local candidates, not a full-world scan or per-agent global optimizer.
        var candidates = new List<Vec2>();
        for (int dy = -6; dy <= 6; dy++)
            for (int dx = -6; dx <= 6; dx++)
            {
                var p = new Vec2((int)home.Position.X + dx, (int)home.Position.Y + dy);
                if (p != Clamp(p) || !HaulPassable((int)p.X, (int)p.Y) || AvailableResource(resource, p) < .1f ||
                    haulJobs.Any(j => j.Cargo == 0 && j.Resource == resource && j.Source == p) ||
                    haulSourceFailures.Any(f => f.TribeId == tribe.Id && f.Resource == resource && f.Source == p && f.UntilTick > Tick)) continue;
                candidates.Add(p);
            }
        foreach (var source in candidates.OrderBy(p => DistanceSquared(worker.Position, p)).ThenBy(p => p.Y).ThenBy(p => p.X).Take(4))
        {
            if (searchBudget <= 0) return;
            var path = GridPathfinder.Search(Terrain, worker.Position, source, (x,y) => !HaulPassable(x,y), Math.Min(512, searchBudget));
            searchBudget -= path.ExpandedNodes;
            if (path.Status != PathSearchStatus.Found)
            {
                // Worker reachability is not globally transferable; only remember a proven bad source after a commitment expires.
                continue;
            }
            haulJobs.Add(new() { WorkerId = worker.Id, TribeId = tribe.Id, Resource = resource,
                Source = source, Home = home.Position, Route = path.Path.ToArray(), Stage = HaulStage.Outbound,
                DeadlineTick = Tick + 1800 });
            return;
        }
        }
    }

    private bool TryHaulMotion(Agent agent, float dt, out Vec2 motion)
    {
        motion = default;
        var job = haulJobs.FirstOrDefault(j => j.WorkerId == agent.Id);
        if (job is null || agent.Energy < .35f || Tick >= job.DeadlineTick) return false;
        if (job.Stage is HaulStage.Blocked or HaulStage.WaitingForCapacity) return true;
        while (job.Waypoint < job.Route.Length && DistanceSquared(agent.Position, job.Route[job.Waypoint]) < .04f) job.Waypoint++;
        if (job.Waypoint >= job.Route.Length) return true;
        var next = job.Route[job.Waypoint];
        if (!HaulPassable((int)next.X, (int)next.Y))
        { job.Stage = HaulStage.Blocked; job.BlockReason = "route-blocked"; job.RetryTick = Tick + 30; return true; }
        var delta = new Vec2(next.X - agent.Position.X, next.Y - agent.Position.Y);
        float length = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        float speed = agent.Genome.Speed * 4 * dt;
        motion = delta * (1 / Math.Max(length, speed));
        return true;
    }

    private void StepHauling(bool cleanupOnly = false)
    {
        haulSourceFailures.RemoveAll(f => f.UntilTick <= Tick || !tribes.Any(t => t.Id == f.TribeId));
        for (int i = haulJobs.Count - 1; i >= 0; i--)
        {
            var job = haulJobs[i];
            var worker = Agents.FirstOrDefault(a => a.Id == job.WorkerId);
            var tribe = tribes.FirstOrDefault(t => t.Id == job.TribeId);
            if (worker is null || worker.Health <= 0 || worker.Energy <= 0 || tribe is null || !tribe.MemberIds.Contains(job.WorkerId) ||
                !structures.Any(s => s.Kind == StructureKind.Hut && s.TribeId == job.TribeId && s.Position == job.Home))
            { LostHaulCargo += job.Cargo; haulJobs.RemoveAt(i); continue; }
            if (cleanupOnly) continue;
            if (worker.Energy < .35f || Tick >= job.DeadlineTick)
            {
                if (Tick >= job.DeadlineTick && job.Stage == HaulStage.Blocked && job.BlockReason != "return-route")
                    RememberHaulFailure(job.TribeId, job.Resource, job.Source);
                LostHaulCargo += job.Cargo;
                RecordEvent($"Сборщик {worker.Id}: работа отменена ({(worker.Energy < .35f ? "истощение" : "срок ожидания")}), потерян груз {job.Cargo:0.00}.");
                haulJobs.RemoveAt(i);
                continue;
            }
            if (job.Stage == HaulStage.Blocked)
            {
                if (Tick < job.RetryTick) continue;
                var goal = job.Cargo > 0 ? job.Home : job.Source;
                var path = GridPathfinder.Search(Terrain, worker.Position, goal, (x,y) => !HaulPassable(x,y), 512);
                if (path.Status != PathSearchStatus.Found)
                { job.BlockReason = path.Status.ToString(); job.RetryTick = Tick + 30; continue; }
                job.Route = path.Path.ToArray(); job.Waypoint = 0;
                job.Stage = job.Cargo > 0 ? HaulStage.Returning : HaulStage.Outbound; job.BlockReason = "";
            }
            var destination = job.Cargo > 0 ? job.Home : job.Source;
            if (DistanceSquared(worker.Position, destination) >= .16f) continue;
            if (job.Cargo <= 0)
            {
                job.Cargo = Math.Min(.25f, AvailableResource(job.Resource, job.Source));
                if (job.Cargo <= 0) { haulJobs.RemoveAt(i); continue; }
                int x = (int)job.Source.X, y = (int)job.Source.Y;
                var c = Environment.Get(x,y);
                if (job.Resource == HaulResource.Food) Food.Set(x,y,Food.Get(x,y)-job.Cargo);
                else Environment.Set(x,y, job.Resource == HaulResource.Wood ? c with { Wood = c.Wood-job.Cargo } : c with { Stone = c.Stone-job.Cargo });
                if (job.Resource == HaulResource.Food) tribe.FoodKnowledge = Math.Min(CivilizationProgression.MaximumKnowledge, tribe.FoodKnowledge+job.Cargo);
                else if (job.Resource == HaulResource.Wood) tribe.WoodKnowledge = Math.Min(CivilizationProgression.MaximumKnowledge, tribe.WoodKnowledge+job.Cargo);
                else tribe.StoneKnowledge = Math.Min(CivilizationProgression.MaximumKnowledge, tribe.StoneKnowledge+job.Cargo);
                job.Route = []; job.Waypoint = 0;
                job.Stage = HaulStage.Blocked; job.BlockReason = "return-route"; job.RetryTick = Tick;
            }
            else
            {
                float stock = job.Resource == HaulResource.Food ? tribe.Food : job.Resource == HaulResource.Wood ? tribe.Wood : tribe.Stone;
                float delivered = Math.Min(job.Cargo, Math.Max(0, 100-stock));
                if (job.Resource == HaulResource.Food) tribe.Food += delivered;
                else if (job.Resource == HaulResource.Wood) tribe.Wood += delivered;
                else tribe.Stone += delivered;
                job.Cargo -= delivered; DeliveredHaulCargo += delivered;
                if (job.Cargo <= .000001f) haulJobs.RemoveAt(i);
                else { job.Stage = HaulStage.WaitingForCapacity; job.BlockReason = "storage-full"; }
            }
        }
    }
}

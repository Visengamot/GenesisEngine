namespace Genesis.Engine.Core;

public sealed class MealJourney
{
    public int WorkerId { get; set; }
    public int TribeId { get; set; }
    public Vec2 Home { get; set; }
    public Vec2[] Route { get; set; } = [];
    public int Waypoint { get; set; }
    public long DeadlineTick { get; set; }
}

public sealed partial class Simulation
{
    private readonly List<MealJourney> mealJourneys = new();
    public IReadOnlyList<MealJourney> MealJourneys => mealJourneys.AsReadOnly();

    private bool HasWorkAssignment(int id) => haulJobs.Any(j => j.WorkerId == id) || constructionSites.Any(s => s.WorkerId == id);

    private void CleanMealJourneys()
    {
        mealJourneys.RemoveAll(j => Tick >= j.DeadlineTick || HasWorkAssignment(j.WorkerId) ||
            !Agents.Any(a => a.Id == j.WorkerId && a.Health > 0 && a.Energy > 0 && a.Energy < 1.5f) ||
            !tribes.Any(t => t.Id == j.TribeId && t.Food > .15f && t.MemberIds.Contains(j.WorkerId)) ||
            !structures.Any(s => s.TribeId == j.TribeId && s.Kind == StructureKind.Hut && s.Position == j.Home));
    }

    private void AssignMealJourney(Tribe tribe, Agent[] members)
    {
        if (tribe.Food <= .15f || mealJourneys.Count >= 128) return;
        var candidates = members.Where(a => a.Health > 0 && a.Energy > 0 && a.Energy < 1.2f &&
            !HasWorkAssignment(a.Id) && !mealJourneys.Any(j => j.WorkerId == a.Id))
            .OrderBy(a => a.Energy).ThenBy(a => a.Id).Take(4).ToArray();
        int searchBudget = 2048;
        // One assignment; at most four citizens/four homes, sharing one expansion budget.
        foreach (var citizen in candidates)
        {
        foreach (var hut in structures.Where(s => s.TribeId == tribe.Id && s.Kind == StructureKind.Hut &&
            mealJourneys.Count(j => j.TribeId == tribe.Id && j.Home == s.Position) < ResidentsPerHut)
            .OrderBy(s => DistanceSquared(citizen.Position, s.Position)).ThenBy(s => s.Position.Y).ThenBy(s => s.Position.X).Take(4))
        {
            if (searchBudget <= 0) return;
            var path = GridPathfinder.Search(Terrain, citizen.Position, hut.Position, (x,y) => !HaulPassable(x,y), Math.Min(512, searchBudget));
            searchBudget -= path.ExpandedNodes;
            if (path.Status != PathSearchStatus.Found) continue;
            mealJourneys.Add(new() { WorkerId = citizen.Id, TribeId = tribe.Id, Home = hut.Position,
                Route = path.Path.ToArray(), DeadlineTick = Tick + 900 });
            return;
        }
        }
    }

    private bool TryMealMotion(Agent citizen, float dt, out Vec2 motion)
    {
        motion = default;
        MealJourney? journey = null;
        foreach (var candidate in mealJourneys)
            if (candidate.WorkerId == citizen.Id) { journey = candidate; break; }
        if (journey is null || citizen.Energy >= 1.5f || Tick >= journey.DeadlineTick) return false;
        while (journey.Waypoint < journey.Route.Length && DistanceSquared(citizen.Position, journey.Route[journey.Waypoint]) < .04f)
            journey.Waypoint++;
        if (journey.Waypoint >= journey.Route.Length) return true;
        var next = journey.Route[journey.Waypoint];
        if (!HaulPassable((int)next.X, (int)next.Y))
        { mealJourneys.Remove(journey); return false; }
        var delta = new Vec2(next.X-citizen.Position.X, next.Y-citizen.Position.Y);
        float length = MathF.Sqrt(delta.X*delta.X+delta.Y*delta.Y);
        motion = delta * (1 / Math.Max(length, citizen.Genome.Speed*4*dt));
        return true;
    }

    private void RestoreMealJourneys(WorldSave save)
    {
        if (save.Version < 17) return;
        if (save.MealJourneys is null || save.MealJourneys.Length > 128) throw new ArgumentException("Invalid meal journey count.");
        var workers = new HashSet<int>();
        foreach (var j in save.MealJourneys)
        {
            if (j is null || !workers.Add(j.WorkerId) || j.DeadlineTick <= 0 || HasWorkAssignment(j.WorkerId) ||
                !Agents.Any(a => a.Id == j.WorkerId) || !tribes.Any(t => t.Id == j.TribeId && t.MemberIds.Contains(j.WorkerId)) ||
                !structures.Any(s => s.TribeId == j.TribeId && s.Kind == StructureKind.Hut && s.Position == j.Home) ||
                j.Route is null || j.Route.Length == 0 || j.Route.Length > 513 || j.Waypoint < 0 || j.Waypoint > j.Route.Length)
                throw new ArgumentException("Invalid meal journey.");
            foreach (var p in j.Route)
            { Guard.Position(p, nameof(save)); if (p != Clamp(p) || p.X != (int)p.X || p.Y != (int)p.Y) throw new ArgumentException("Invalid meal route."); }
            for (int i = 1; i < j.Route.Length; i++)
                if (Math.Abs(j.Route[i].X-j.Route[i-1].X)+Math.Abs(j.Route[i].Y-j.Route[i-1].Y) != 1) throw new ArgumentException("Nonadjacent meal route.");
            if (j.Route[^1] != j.Home) throw new ArgumentException("Wrong meal destination.");
            mealJourneys.Add(j);
        }
        if (mealJourneys.GroupBy(j => (j.TribeId,j.Home)).Any(g => g.Count() > ResidentsPerHut))
            throw new ArgumentException("Meal destination overbooked.");
    }
}

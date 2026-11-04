namespace Genesis.Engine.Core;

public enum StructureKind { Hut, Farm, Road }
public enum TribalRelation { Neutral, Alliance, War }
public sealed record Structure(StructureKind Kind, Vec2 Position, int TribeId);
public sealed record TribeRelation(int FirstTribeId, int SecondTribeId, TribalRelation Relation);

/// <summary>Small rule-based settlements with resource-earned, per-tribe cultural progression.</summary>
public sealed class Tribe
{
    public int Id { get; internal set; }
    public Vec2 Center { get; internal set; }
    public List<int> MemberIds { get; } = new();
    public float Wood { get; internal set; }
    public float Stone { get; internal set; }
    public float Food { get; internal set; }
    public bool HasWeapons { get; internal set; }
    public bool HasFarming { get; internal set; }
    public bool HasToolmaking { get; internal set; }
    public bool HasMedicine { get; internal set; }
    public bool HasFireproofing { get; internal set; }
    public float Age { get; internal set; }
    // Cumulative experience comes only from goods actually harvested from world cells.
    public float FoodKnowledge { get; internal set; }
    public float WoodKnowledge { get; internal set; }
    public float StoneKnowledge { get; internal set; }

    /// <summary>Current civilization stage; known inventions, not time elapsed, determine the stage.</summary>
    public CivilizationPhase Phase => HasFireproofing && HasMedicine ? CivilizationPhase.Advanced :
        HasMedicine || HasFireproofing || HasToolmaking ? CivilizationPhase.Skilled :
        HasFarming || HasWeapons ? CivilizationPhase.Settled : CivilizationPhase.Early;

    /// <summary>The next eligible invention in the deterministic research order.</summary>
    public CivilizationMilestone NextMilestone => CivilizationProgression.NextMilestone(this);

    /// <summary>Experience and available investment towards that invention (0 to 1).</summary>
    public float MilestoneProgress => CivilizationProgression.Progress(this, NextMilestone);

    /// <summary>Ordered, read-only technology tree for UI, including locked prerequisites.</summary>
    public IReadOnlyList<CivilizationResearch> Research => CivilizationProgression.Tree(this);
}

public sealed partial class Simulation
{
    private readonly List<Tribe> tribes = new();
    private readonly List<Structure> structures = new();
    private readonly List<TribeRelation> relations = new();
    public IReadOnlyList<Tribe> Tribes => tribes.AsReadOnly();
    public IReadOnlyList<Structure> Structures => structures.AsReadOnly();
    public IReadOnlyList<TribeRelation> Relations => relations.AsReadOnly();
    private readonly List<string> events = new();
    public IReadOnlyList<string> Events => events.AsReadOnly();
    private void RecordEvent(string message) { if (events.Count >= 64) events.RemoveAt(0); events.Add($"Тик {Tick}: {message}"); }
    private int nextTribeId;
    private float civilizationClock;

    partial void StepCivilization(float dt)
    {
        civilizationClock += dt;
        // Step accepts at most one second, so a valid remainder needs at most one decision.
        // Retain fractional time instead of losing up to one tick at every decision.
        if (civilizationClock < 1) return;
        const float elapsed = 1;
        civilizationClock -= 1;
        var social = Agents.Where(a => a.Genome.Sociality >= .5f && a.Energy > 0).OrderBy(a => a.Id).ToArray();
        // Citizenship survives changing social traits and generational turnover.
        // Sociality gates founding/joining only, not continued membership.
        var available = Agents.Where(a => a.Energy > 0).ToDictionary(a => a.Id);
        foreach (var t in tribes) t.MemberIds.RemoveAll(id => !available.ContainsKey(id));
        tribes.RemoveAll(t => t.MemberIds.Count == 0);
        // Extinct settlements cannot reserve scarce structure slots or retain stale diplomacy.
        var survivingIds = tribes.Select(t => t.Id).ToHashSet();
        structures.RemoveAll(s => !survivingIds.Contains(s.TribeId));
        relations.RemoveAll(r => !survivingIds.Contains(r.FirstTribeId) || !survivingIds.Contains(r.SecondTribeId));
        var assigned = tribes.SelectMany(t => t.MemberIds).ToHashSet();
        foreach (var a in social)
        {
            if (assigned.Contains(a.Id)) continue;
            var tribe = tribes.Where(t => DistanceSquared(t.Center, a.Position) <= 100).MinBy(t => DistanceSquared(t.Center, a.Position));
            if (tribe is null)
            {
                var neighbors = social.Where(b => !assigned.Contains(b.Id) && DistanceSquared(a.Position, b.Position) <= 36).Take(12).ToArray();
                if (neighbors.Length < 3 || tribes.Count >= 32) continue;
                tribe = new Tribe { Id = nextTribeId++, Center = a.Position };
                tribes.Add(tribe); RecordEvent($"Основано племя {tribe.Id}.");
                foreach (var b in neighbors) { tribe.MemberIds.Add(b.Id); assigned.Add(b.Id); }
            }
            else { tribe.MemberIds.Add(a.Id); assigned.Add(a.Id); }
        }
        foreach (var t in tribes)
        {
            var members = t.MemberIds.Select(id => available[id]).ToArray();
            t.Age += elapsed;
            // Once built, a settlement stays at its hut rather than following wandering citizens.
            var home = structures.FirstOrDefault(s => s.TribeId == t.Id && s.Kind == StructureKind.Hut);
            t.Center = home?.Position ?? t.Center;
            if (home is not null)
                using (Profiler?.Measure(SimulationPhase.HaulAssignment, members.Length)) AssignHauler(t, members, home);
            using (Profiler?.Measure(SimulationPhase.MealAssignment, members.Length)) AssignMealJourney(t, members);
            // Bootstrap gathering remains only until the first physical home exists.
            foreach (var a in home is null ? members : Array.Empty<Agent>())
            {
                int x = (int)a.Position.X, y = (int)a.Position.Y;
                var c = Environment.Get(x, y);
                float wood = Math.Min(c.Wood, .025f * elapsed), stone = Math.Min(c.Stone, .015f * elapsed);
                float food = Math.Min(Food.Get(x, y), .01f * elapsed);
                Environment.Set(x, y, c with { Wood = c.Wood - wood, Stone = c.Stone - stone });
                Food.Set(x, y, Food.Get(x, y) - food);
                t.Wood = Math.Min(100, t.Wood + wood); t.Stone = Math.Min(100, t.Stone + stone); t.Food = Math.Min(100, t.Food + food);
                // No experience for unharvested stock, elapsed age, or goods received by trade.
                t.WoodKnowledge = Math.Min(CivilizationProgression.MaximumKnowledge, t.WoodKnowledge + wood);
                t.StoneKnowledge = Math.Min(CivilizationProgression.MaximumKnowledge, t.StoneKnowledge + stone);
                t.FoodKnowledge = Math.Min(CivilizationProgression.MaximumKnowledge, t.FoodKnowledge + food);
            }
            AdvanceCivilization(t);
            Build(t, StructureKind.Hut, .6f, .2f, true);
            Build(t, StructureKind.Farm, .5f, .1f, t.HasFarming);
            BuildRoad(t);
            foreach (var s in structures.Where(s => s.TribeId == t.Id))
            {
                int x = (int)s.Position.X, y = (int)s.Position.Y;
                if (s.Kind == StructureKind.Farm) Food.Set(x, y, Math.Min(1, Food.Get(x, y) + elapsed * .12f * EnvironmentGrid.Fertility(Environment.Get(x, y))));
                // Roads connect buildings by obstacle-aware routes; no free energy bonus.
            }
            using (Profiler?.Measure(SimulationPhase.MealService, members.Length)) ServeMeals(t, members, elapsed);
            TreatExposure(t, members, elapsed);
            MaintainFirebreak(t, elapsed);
            HuntPredators(t, members, elapsed);
        }
        var previousRelations = relations.ToArray();
        relations.Clear();
        for (int i = 0; i < tribes.Count; i++)
            for (int j = i + 1; j < tribes.Count; j++)
            {
                var a = tribes[i]; var b = tribes[j];
                float aggression = (a.MemberIds.Average(id => available[id].Genome.Aggression) + b.MemberIds.Average(id => available[id].Genome.Aggression)) / 2;
                var relation = DistanceSquared(a.Center, b.Center) > 400 ? TribalRelation.Neutral : aggression > .65f ? TribalRelation.War : aggression < .4f ? TribalRelation.Alliance : TribalRelation.Neutral;
                relations.Add(new(a.Id, b.Id, relation));
                JournalRelation(a, b, relation, previousRelations);
                if (relation == TribalRelation.Alliance) TradeAlongRoad(a, b, elapsed);
                if (relation == TribalRelation.War)
                {
                    // Explicit proximity attrition, not tactical combat or diplomacy.
                    foreach (int id in a.MemberIds.Concat(b.MemberIds))
                    {
                        var member = available[id]; var enemy = a.MemberIds.Contains(id) ? b : a;
                        if (DistanceSquared(member.Position, enemy.Center) < 16) Damage(member, elapsed * (enemy.HasWeapons ? .04f : .02f), DamageKind.Conflict);
                    }
                }
            }
    }
    private void BuildRoad(Tribe tribe)
    {
        if (!tribe.HasFarming || !tribe.HasWeapons || tribe.Stone < .04f || tribe.Wood < .02f || structures.Count + constructionSites.Count >= 128 || structures.Count(s => s.Kind == StructureKind.Road) >= 64) return;
        var hut = structures.FirstOrDefault(s => s.TribeId == tribe.Id && s.Kind == StructureKind.Hut);
        var farm = structures.FirstOrDefault(s => s.TribeId == tribe.Id && s.Kind == StructureKind.Farm);
        if (hut is null || farm is null) return;
        var path = GridPathfinder.Find(Terrain, hut.Position, farm.Position,
            (x,y) => Environment.Get(x,y).Fire >= .05f);
        // Lay one paid segment per decision, rather than materializing an entire free road.
        foreach (var point in path.Skip(1).SkipLast(1))
        {
            if (structures.Any(s => s.Position == point) || constructionSites.Any(s => s.Position == point)) continue;
            tribe.Stone -= .04f; tribe.Wood -= .02f;
            structures.Add(new(StructureKind.Road, point, tribe.Id));
            return;
        }
    }

    public const int ResidentsPerHut = 8;
    public int HousingCapacity(int tribeId) => structures.Count(s => s.TribeId == tribeId && s.Kind == StructureKind.Hut) * ResidentsPerHut;

    private void Build(Tribe t, StructureKind kind, float wood, float stone, bool enabled)
    {
        int desired = kind == StructureKind.Hut ? Math.Clamp((t.MemberIds.Count + ResidentsPerHut - 1) / ResidentsPerHut, 1, 8) : 1;
        int existing = structures.Count(s => s.TribeId == t.Id && s.Kind == kind);
        if (!enabled || existing >= desired || constructionSites.Any(s => s.TribeId == t.Id && s.Kind == kind) || t.Wood < wood || t.Stone < stone) return;
        Vec2? site = null;
        int searchRadius = kind == StructureKind.Farm ? 5 : Math.Min(5, 2 + existing);
        for (int dy = -searchRadius; dy <= searchRadius && site is null; dy++)
            for (int dx = -searchRadius; dx <= searchRadius && site is null; dx++)
            {
                var candidate = Clamp(new((int)t.Center.X + dx, (int)t.Center.Y + dy));
                float height = Terrain.Get((int)candidate.X, (int)candidate.Y);
                bool spaced = kind != StructureKind.Farm || !structures.Any(s => s.TribeId == t.Id && s.Kind == StructureKind.Hut && DistanceSquared(s.Position, candidate) < 9);
                if (spaced && height >= 0 && height < 1.5f && Environment.Get((int)candidate.X, (int)candidate.Y).Fire < .05f && !structures.Any(s => s.Position == candidate) && !constructionSites.Any(s => s.Position == candidate)) site = candidate;
            }
        if (site is null) return;
        // Old saves may already have all 128 slots filled by roads. Reclaim one road for housing/food.
        if (structures.Count + constructionSites.Count >= 128)
        {
            int road = structures.FindLastIndex(s => s.Kind == StructureKind.Road);
            if (road < 0) return;
            structures.RemoveAt(road);
        }
        t.Wood -= wood; t.Stone -= stone;
        var warehouse = structures.FirstOrDefault(s => s.TribeId == t.Id && s.Kind == StructureKind.Hut);
        constructionSites.Add(new() { Kind = kind, Position = site.Value, TribeId = t.Id,
            PaidWood = wood, PaidStone = stone, BlockReason = "no-worker",
            MaterialsDelivered = warehouse is null, DeliveredMaterialAmount = warehouse is null ? wood + stone : 0,
            MaterialOrigin = warehouse?.Position ?? site.Value, MaterialPosition = warehouse?.Position ?? site.Value });
        string name = kind == StructureKind.Hut ? "хижину" : kind == StructureKind.Farm ? "ферму" : "дорогу";
        RecordEvent($"Племя {t.Id} начало строить {name}.");
    }
}

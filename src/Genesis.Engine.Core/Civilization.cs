namespace Genesis.Engine.Core;

public enum StructureKind { Hut, Farm, Road }
public enum TribalRelation { Neutral, Alliance, War }
public sealed record Structure(StructureKind Kind, Vec2 Position, int TribeId);
public sealed record TribeRelation(int FirstTribeId, int SecondTribeId, TribalRelation Relation);

/// <summary>Small rule-based settlements, not an emergent historical or political model.</summary>
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
    public float Age { get; internal set; }
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
        // Fixed one-second decision cadence, with bounded resource rates between decisions.
        if (civilizationClock < 1) return;
        float elapsed = civilizationClock; civilizationClock = 0;
        var social = Agents.Where(a => a.Genome.Sociality >= .5f && a.Energy > 0).OrderBy(a => a.Id).ToArray();
        var available = social.ToDictionary(a => a.Id);
        foreach (var t in tribes) t.MemberIds.RemoveAll(id => !available.ContainsKey(id));
        tribes.RemoveAll(t => t.MemberIds.Count == 0);
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
            t.Center = new(members.Average(a => a.Position.X), members.Average(a => a.Position.Y));
            foreach (var a in members)
            {
                int x = (int)a.Position.X, y = (int)a.Position.Y;
                var c = Environment.Get(x, y);
                float wood = Math.Min(c.Wood, .025f * elapsed), stone = Math.Min(c.Stone, .015f * elapsed);
                float food = Math.Min(Food.Get(x, y), .01f * elapsed);
                Environment.Set(x, y, c with { Wood = c.Wood - wood, Stone = c.Stone - stone });
                Food.Set(x, y, Food.Get(x, y) - food);
                t.Wood = Math.Min(100, t.Wood + wood); t.Stone = Math.Min(100, t.Stone + stone); t.Food = Math.Min(100, t.Food + food);
            }
            if (!t.HasFarming && t.Age >= 10 && t.Wood >= .5f && t.Food >= .1f) { t.Wood -= .5f; t.Food -= .1f; t.HasFarming = true; RecordEvent($"Племя {t.Id} освоило земледелие."); }
            if (!t.HasWeapons && t.Age >= 15 && t.Wood >= .3f && t.Stone >= .5f) { t.Wood -= .3f; t.Stone -= .5f; t.HasWeapons = true; RecordEvent($"Племя {t.Id} освоило оружие."); }
            Build(t, StructureKind.Hut, .6f, .2f, true);
            Build(t, StructureKind.Farm, .5f, .1f, t.HasFarming);
            BuildRoad(t);
            foreach (var s in structures.Where(s => s.TribeId == t.Id))
            {
                int x = (int)s.Position.X, y = (int)s.Position.Y;
                if (s.Kind == StructureKind.Farm) Food.Set(x, y, Math.Min(1, Food.Get(x, y) + elapsed * .12f * EnvironmentGrid.Fertility(Environment.Get(x, y))));
                if (s.Kind == StructureKind.Hut)
                    foreach (var a in members.Where(a => DistanceSquared(a.Position, s.Position) < 4))
                    { float meal = Math.Min(Math.Max(0, t.Food - .15f), .02f * elapsed); t.Food -= meal; a.Energy = Math.Min(3, a.Energy + meal); }
                // Roads connect buildings by obstacle-aware routes; no free energy bonus.
            }
            if (t.HasWeapons)
                foreach (var p in predators.Where(p => DistanceSquared(p.Position, t.Center) < 16)) p.Energy = Math.Max(0, p.Energy - .08f * elapsed);
        }
        relations.Clear();
        for (int i = 0; i < tribes.Count; i++)
            for (int j = i + 1; j < tribes.Count; j++)
            {
                var a = tribes[i]; var b = tribes[j];
                float aggression = (a.MemberIds.Average(id => available[id].Genome.Aggression) + b.MemberIds.Average(id => available[id].Genome.Aggression)) / 2;
                var relation = DistanceSquared(a.Center, b.Center) > 400 ? TribalRelation.Neutral : aggression > .65f ? TribalRelation.War : aggression < .4f ? TribalRelation.Alliance : TribalRelation.Neutral;
                relations.Add(new(a.Id, b.Id, relation));
                if (relation == TribalRelation.Alliance)
                { float transfer = Math.Clamp((a.Food - b.Food) * .1f * elapsed, -b.Food, a.Food); a.Food -= transfer; b.Food += transfer; }
                if (relation == TribalRelation.War)
                {
                    // Explicit proximity attrition, not tactical combat or diplomacy.
                    foreach (int id in a.MemberIds.Concat(b.MemberIds))
                    {
                        var member = available[id]; var enemy = a.MemberIds.Contains(id) ? b : a;
                        if (DistanceSquared(member.Position, enemy.Center) < 16) member.Energy = Math.Max(0, member.Energy - elapsed * (enemy.HasWeapons ? .04f : .02f));
                    }
                }
            }
    }
    private void BuildRoad(Tribe tribe)
    {
        if (!tribe.HasFarming || !tribe.HasWeapons || tribe.Stone < .04f || tribe.Wood < .02f || structures.Count >= 128) return;
        var hut = structures.FirstOrDefault(s => s.TribeId == tribe.Id && s.Kind == StructureKind.Hut);
        var farm = structures.FirstOrDefault(s => s.TribeId == tribe.Id && s.Kind == StructureKind.Farm);
        if (hut is null || farm is null) return;
        var path = GridPathfinder.Find(Terrain, hut.Position, farm.Position,
            (x,y) => Environment.Get(x,y).Fire >= .05f);
        // Lay one paid segment per decision, rather than materializing an entire free road.
        foreach (var point in path.Skip(1).SkipLast(1))
        {
            if (structures.Any(s => s.Position == point)) continue;
            tribe.Stone -= .04f; tribe.Wood -= .02f;
            structures.Add(new(StructureKind.Road, point, tribe.Id));
            return;
        }
    }

    private void Build(Tribe t, StructureKind kind, float wood, float stone, bool enabled)
    {
        if (!enabled || structures.Count >= 128 || structures.Any(s => s.TribeId == t.Id && s.Kind == kind) || t.Wood < wood || t.Stone < stone) return;
        Vec2? site = null;
        int searchRadius = kind == StructureKind.Farm ? 5 : 2;
        for (int dy = -searchRadius; dy <= searchRadius && site is null; dy++)
            for (int dx = -searchRadius; dx <= searchRadius && site is null; dx++)
            {
                var candidate = Clamp(new((int)t.Center.X + dx, (int)t.Center.Y + dy));
                float height = Terrain.Get((int)candidate.X, (int)candidate.Y);
                bool spaced = kind != StructureKind.Farm || !structures.Any(s => s.TribeId == t.Id && s.Kind == StructureKind.Hut && DistanceSquared(s.Position, candidate) < 9);
                if (spaced && height >= 0 && height < 1.5f && Environment.Get((int)candidate.X, (int)candidate.Y).Fire < .05f && !structures.Any(s => s.Position == candidate)) site = candidate;
            }
        if (site is null) return;
        t.Wood -= wood; t.Stone -= stone;
        structures.Add(new(kind, site.Value, t.Id));
        string name = kind == StructureKind.Hut ? "хижину" : kind == StructureKind.Farm ? "ферму" : "дорогу";
        RecordEvent($"Племя {t.Id} построило {name}.");
    }
}

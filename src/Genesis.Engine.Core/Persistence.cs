using System.Text.Json;

namespace Genesis.Engine.Core;

public sealed partial class Simulation
{
    private static readonly JsonSerializerOptions SaveOptions = new() { WriteIndented = false, MaxDepth = 64 };

    /// <summary>Versioned world snapshot including brains, random-generator state and journal.</summary>
    public string SaveJson()
    {
        ValidateAgents();
        RefreshSpecies();
        var livingIds = Agents.Select(a => a.Id).ToHashSet();
        var data = new WorldSave
        {
            Version = 15,
            ConstructionSites = constructionSites.ToArray(),
            HaulSourceFailures = haulSourceFailures.ToArray(),
            HaulJobs = haulJobs.ToArray(),
            LostHaulCargo = LostHaulCargo,
            DeliveredHaulCargo = DeliveredHaulCargo,
            PendingCommands = pendingCommands.ToArray(),
            CommandResults = commandResults.ToArray(),
            NextCommandSequence = nextCommandSequence,
            PopulationLimit = PopulationLimit,
            Mortality = Mortality,
            NextSpeciesId = nextSpeciesId,
            Lineages = SaveLineages(),
            RandomState = random.State,
            Events = events.ToArray(),
            Width = Terrain.Width,
            Height = Terrain.Height,
            Seed = Seed,
            Tick = Tick,
            NextId = nextId,
            AllowInterspeciesMating = AllowInterspeciesMating,
            NextTribeId = nextTribeId,
            CivilizationClock = civilizationClock,
            Terrain = new float[Terrain.Width * Terrain.Height],
            Food = new float[Terrain.Width * Terrain.Height],
            Environment = new EnvironmentCell[Terrain.Width * Terrain.Height],
            Agents = Agents.Select(a => new AgentSave
            {
                Id = a.Id,
                SpeciesId = a.SpeciesId,
                Position = a.Position,
                Energy = a.Energy,
                Health = a.Health,
                Age = a.Age,
                Generation = a.Generation,
                OffspringCount = a.OffspringCount,
                Heading = a.Heading,
                Traits = [a.Genome.Speed, a.Genome.Size, a.Genome.Vision, a.Genome.Metabolism, a.Genome.Aggression, a.Genome.Sociality, a.Genome.Curiosity],
                Brain = a.Brain.ToSnapshot()
            }).ToArray(),
            Predators = predators.Select(p => new PredatorSave { Position = p.Position, Energy = p.Energy }).ToArray(),
            Tribes = tribes.Select(t => new TribeSave
            {
                Id = t.Id,
                Center = t.Center,
                Members = t.MemberIds.Where(livingIds.Contains).ToArray(),
                Wood = t.Wood,
                Stone = t.Stone,
                Food = t.Food,
                Age = t.Age,
                FoodKnowledge = t.FoodKnowledge,
                WoodKnowledge = t.WoodKnowledge,
                StoneKnowledge = t.StoneKnowledge,
                HasWeapons = t.HasWeapons,
                HasFarming = t.HasFarming,
                HasToolmaking = t.HasToolmaking,
                HasMedicine = t.HasMedicine,
                HasFireproofing = t.HasFireproofing
            }).ToArray(),
            Structures = structures.ToArray(),
            Relations = relations.ToArray()
        };
        for (int y = 0; y < Terrain.Height; y++)
            for (int x = 0; x < Terrain.Width; x++)
            { int i = y * Terrain.Width + x; data.Terrain[i] = Terrain.Get(x, y); data.Food[i] = Food.Get(x, y); data.Environment[i] = Environment.Get(x, y); }
        return JsonSerializer.Serialize(data, SaveOptions);
    }

    public static Simulation LoadJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > 128_000_000) throw new ArgumentException("Snapshot exceeds size limit.", nameof(json));
        var d = JsonSerializer.Deserialize<WorldSave>(json, SaveOptions) ?? throw new ArgumentException("Empty snapshot.", nameof(json));
        if (d.Version is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15)) throw new NotSupportedException($"Unsupported world save version {d.Version}.");
        if (d.Version >= 6 && (d.PopulationLimit < MinimumPopulationLimit || d.PopulationLimit > AbsoluteMaximumPopulation))
            throw new ArgumentException("Invalid population limit.");
        if (d.Version >= 5)
        {
            var m = d.Mortality ?? throw new ArgumentException("Missing mortality counters.");
            if (m.LifetimeDeaths < 0 || m.OldAge < 0 || m.EnergyDepletion < 0 || m.Fire < 0 ||
                m.Predators < 0 || m.Climate < 0 || m.Other < 0 ||
                (decimal)m.OldAge + m.EnergyDepletion + m.Fire + m.Predators + m.Climate + m.Other != m.LifetimeDeaths)
                throw new ArgumentException("Invalid mortality counters.");
        }
        if (d.Version >= 2 && (d.RandomState is null || d.Events is null || d.Events.Length > 64 || d.Events.Any(e => e is null || e.Length > 2048)))
            throw new ArgumentException("Invalid random state or journal.");
        if (d.Width <= 0 || d.Height <= 0 || (long)d.Width * d.Height > 1_000_000) throw new ArgumentException("Invalid snapshot dimensions.");
        int count = d.Width * d.Height;
        if (d.Terrain?.Length != count || d.Food?.Length != count || d.Environment?.Length != count || d.Agents is null || d.Agents.Length > AbsoluteMaximumPopulation ||
            d.Predators is null || d.Predators.Length > 64 || d.Tribes is null || d.Tribes.Length > 32 || d.Structures is null || d.Structures.Length > 128 || d.Relations is null || d.Relations.Length > 496 || d.Tick < 0 || d.NextId < 0 || d.NextId > (long)int.MaxValue + 1 || d.NextTribeId < 0)
            throw new ArgumentException("Invalid snapshot collections or counters.");
        Range(d.CivilizationClock, 0, 2);
        if (d.CivilizationClock >= 1) throw new ArgumentException("Invalid civilization remainder.");
        var sim = new Simulation(d.Width, d.Height, d.Seed);
        sim.SetPopulationLimit(d.Version >= 6 ? d.PopulationLimit : MaximumPopulation);
        sim.Agents.Clear(); sim.Tick = d.Tick; sim.nextId = d.NextId; sim.nextTribeId = d.NextTribeId; sim.civilizationClock = d.CivilizationClock;
        sim.Mortality = d.Version >= 5 ? d.Mortality!.Value : default; // Pre-v5 deaths cannot be inferred reliably.
        for (int y = 0; y < d.Height; y++)
            for (int x = 0; x < d.Width; x++)
            {
                int i = y * d.Width + x; var c = d.Environment[i];
                Range(d.Terrain[i], -100, 100); Range(d.Food[i], 0, 1);
                Range(c.Moisture, 0, 1); Range(c.Temperature, -50, 60); Range(c.Vegetation, 0, 1); Range(c.Wood, 0, 1); Range(c.Stone, 0, 1); Range(c.Fire, 0, 1);
                sim.Terrain.Set(x, y, d.Terrain[i]); sim.Food.Set(x, y, d.Food[i]); sim.Environment.Set(x, y, c);
            }
        foreach (var a in d.Agents)
        {
            if (a is null || a.Traits?.Length != 7 || a.Brain is null || a.Generation < 0 || a.OffspringCount < 0) throw new ArgumentException("Invalid agent snapshot.");
            if (d.Version >= 8) Range(a.Health, 0, 1);
            Range(a.Energy, 0, 3); Range(a.Age, 0, d.Version <= 5 ? 181 : MaximumAge + 1); Guard.Position(a.Heading, nameof(a.Heading));
            float[] g = a.Traits;
            Range(g[0], .1f, 3); Range(g[1], .25f, 3); Range(g[2], 1, 12); Range(g[3], .1f, 3);
            Range(g[4], 0, 1); Range(g[5], 0, 1); Range(g[6], 0, 1);
            var genome = new Genome(g[0], g[1], g[2], g[3], g[4], g[5], g[6]);
            sim.Agents.Add(new(a.Id, a.Position, genome, new NeuralNetwork(a.Brain)) { Health = d.Version >= 8 ? a.Health : 1, Energy = a.Energy, Age = a.Age, Generation = a.Generation, OffspringCount = a.OffspringCount, Heading = a.Heading });
        }
        sim.ValidateAgents();
        sim.AllowInterspeciesMating = d.AllowInterspeciesMating;
        if (d.Version >= 4)
        {
            sim.RestoreLineages(d.Lineages, d.NextSpeciesId);
            foreach (var (agent, saved) in sim.Agents.Zip(d.Agents))
            {
                if (saved.SpeciesId < 0 || !sim.lineages.Any(l => l.Id == saved.SpeciesId &&
                    GeneticCompatibility.Distance(agent.Brain, l.Representative) <= SpeciesCompatibilityThreshold))
                    throw new ArgumentException("Invalid saved species membership.");
                agent.SpeciesId = saved.SpeciesId;
            }
            sim.classifiedAgents = sim.Agents.ToArray();
            // Rebuild summary from the validated saved memberships without reassigning overlap cases.
            var members = sim.Agents.GroupBy(a => a.SpeciesId).ToDictionary(g => g.Key, g => g.Count());
            if (members.Count != sim.lineages.Count || sim.lineages.Any(l => !members.ContainsKey(l.Id)))
                throw new ArgumentException("Lineage snapshot has extinct entries.");
            sim.species = Array.AsReadOnly(sim.lineages.Select(l => new SpeciesSummary(l.Id, members[l.Id])).ToArray());
        }
        else sim.RefreshSpecies();
        var ids = sim.Agents.Select(a => a.Id).ToHashSet(); var tribeIds = new HashSet<int>(); var memberIds = new HashSet<int>();
        foreach (var t in d.Tribes)
        {
            if (t is null || t.Id < 0 || !tribeIds.Add(t.Id) || t.Members is null || t.Members.Length > AbsoluteMaximumPopulation || t.Members.Any(id => !ids.Contains(id) || !memberIds.Add(id))) throw new ArgumentException("Invalid tribe membership.");
            Position(t.Center); Range(t.Wood, 0, 100); Range(t.Stone, 0, 100); Range(t.Food, 0, 100); Range(t.Age, 0, float.MaxValue);
            if (d.Version >= 6) { Range(t.FoodKnowledge, 0, CivilizationProgression.MaximumKnowledge); Range(t.WoodKnowledge, 0, CivilizationProgression.MaximumKnowledge); Range(t.StoneKnowledge, 0, CivilizationProgression.MaximumKnowledge); }
            var tribe = new Tribe { Id = t.Id, Center = t.Center, Wood = t.Wood, Stone = t.Stone, Food = t.Food, Age = t.Age,
                FoodKnowledge = d.Version >= 6 ? t.FoodKnowledge : 0, WoodKnowledge = d.Version >= 6 ? t.WoodKnowledge : 0, StoneKnowledge = d.Version >= 6 ? t.StoneKnowledge : 0,
                HasWeapons = t.HasWeapons, HasFarming = t.HasFarming,
                HasToolmaking = d.Version >= 3 && t.HasToolmaking, HasMedicine = d.Version >= 3 && t.HasMedicine,
                HasFireproofing = d.Version >= 3 && t.HasFireproofing };
            tribe.MemberIds.AddRange(t.Members); sim.tribes.Add(tribe);
            if (t.Id == int.MaxValue) throw new ArgumentException("Tribe ID exhausted.");
            sim.nextTribeId = Math.Max(sim.nextTribeId, t.Id + 1);
        }
        foreach (var p in d.Predators)
        { if (p is null) throw new ArgumentException("Invalid predator."); Position(p.Position); Range(p.Energy, 0, 3); sim.predators.Add(new(p.Position) { Energy = p.Energy }); }
        foreach (var s in d.Structures)
        { if (s is null || !Enum.IsDefined(s.Kind) || s.TribeId < 0 || (d.Version >= 6 && !tribeIds.Contains(s.TribeId))) throw new ArgumentException("Invalid structure."); Position(s.Position); if (tribeIds.Contains(s.TribeId)) sim.structures.Add(s); }
        foreach (var r in d.Relations)
        { if (r is null || !Enum.IsDefined(r.Relation) || !tribeIds.Contains(r.FirstTribeId) || !tribeIds.Contains(r.SecondTribeId) || r.FirstTribeId == r.SecondTribeId) throw new ArgumentException("Invalid relation."); sim.relations.Add(r); }
        if (d.Version >= 2)
        {
            sim.random.State = d.RandomState!.Value;
            sim.events.AddRange(d.Events!);
        }
        sim.RestoreCommands(d);
        sim.RestoreHauling(d);
        sim.RestoreConstruction(d);
        return sim;
        void Position(Vec2 p) { Guard.Position(p, nameof(json)); if (p != sim.Clamp(p)) throw new ArgumentException("Position outside world."); }
    }
    private static void Range(float value, float min, float max)
    { if (!float.IsFinite(value) || value < min || value > max) throw new ArgumentException("Snapshot value outside its valid range."); }
}

// Explicit DTOs keep the save schema independent of live mutable simulation collections.
public sealed class WorldSave
{
    public int Version { get; set; }
    public ulong? RandomState { get; set; }
    public string[]? Events { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Seed { get; set; }
    public long Tick { get; set; }
    public ConstructionSite[] ConstructionSites { get; set; } = [];
    public HaulSourceFailure[] HaulSourceFailures { get; set; } = [];
    public HaulJob[] HaulJobs { get; set; } = [];
    public float LostHaulCargo { get; set; }
    public float DeliveredHaulCargo { get; set; }
    public WorldCommand[] PendingCommands { get; set; } = [];
    public CommandResult[] CommandResults { get; set; } = [];
    public long NextCommandSequence { get; set; }
    public int PopulationLimit { get; set; }
    public MortalityStatistics? Mortality { get; set; }
    public long NextId { get; set; }
    public long NextSpeciesId { get; set; }
    public SpeciesLineageSave[]? Lineages { get; set; }
    public bool AllowInterspeciesMating { get; set; }
    public int NextTribeId { get; set; }
    public float CivilizationClock { get; set; }
    public float[] Terrain { get; set; } = [];
    public float[] Food { get; set; } = [];
    public EnvironmentCell[] Environment { get; set; } = [];
    public AgentSave[] Agents { get; set; } = [];
    public PredatorSave[] Predators { get; set; } = [];
    public TribeSave[] Tribes { get; set; } = [];
    public Structure[] Structures { get; set; } = [];
    public TribeRelation[] Relations { get; set; } = [];
}
public sealed class SpeciesLineageSave
{
    public int Id { get; set; }
    public NetworkSnapshot? Representative { get; set; }
}
public sealed class AgentSave
{
    public int Id { get; set; }
    public int SpeciesId { get; set; }
    public Vec2 Position { get; set; }
    public float[] Traits { get; set; } = [];
    public float Energy { get; set; }
    public float Health { get; set; } = 1;
    public float Age { get; set; }
    public Vec2 Heading { get; set; }
    public int Generation { get; set; }
    public int OffspringCount { get; set; }
    public NetworkSnapshot? Brain { get; set; }
}
public sealed class PredatorSave { public Vec2 Position { get; set; } public float Energy { get; set; } }
public sealed class TribeSave
{
    public int Id { get; set; }
    public Vec2 Center { get; set; }
    public int[] Members { get; set; } = [];
    public float Wood { get; set; }
    public float Stone { get; set; }
    public float Food { get; set; }
    public float Age { get; set; }
    public float FoodKnowledge { get; set; }
    public float WoodKnowledge { get; set; }
    public float StoneKnowledge { get; set; }
    public bool HasWeapons { get; set; }
    public bool HasFarming { get; set; }
    public bool HasToolmaking { get; set; }
    public bool HasMedicine { get; set; }
    public bool HasFireproofing { get; set; }
}

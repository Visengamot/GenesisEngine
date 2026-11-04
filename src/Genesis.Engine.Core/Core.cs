namespace Genesis.Engine.Core;

public readonly record struct Vec2(float X, float Y)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator *(Vec2 a, float scale) => new(a.X * scale, a.Y * scale);
}

internal static class Guard
{
    public static void Finite(float value, string name)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(name, "Must be finite.");
    }

    public static void Position(Vec2 value, string name)
    {
        Finite(value.X, name);
        Finite(value.Y, name);
    }

    public static void Delta(float dt)
    {
        Finite(dt, nameof(dt));
        if (dt < 0 || dt > 1) throw new ArgumentOutOfRangeException(nameof(dt), "Use a time step from zero to one second.");
    }
}

public sealed class TerrainMap
{
    private readonly float[] heights;
    public int Width { get; }
    public int Height { get; }

    public TerrainMap(int w, int height)
    {
        if (w <= 0) throw new ArgumentOutOfRangeException(nameof(w));
        if (height <= 0 || (long)w * height > 16_000_000)
            throw new ArgumentOutOfRangeException(nameof(height), "Map must contain at most 16 million cells.");
        Width = w;
        Height = height;
        heights = new float[w * height];
    }

    // Out-of-map reads return zero and writes are ignored, preserving brush edge behavior.
    public float Get(int x, int y) => Contains(x, y) ? heights[y * Width + x] : 0;
    public void Set(int x, int y, float value)
    {
        Guard.Finite(value, nameof(value));
        if (Contains(x, y)) heights[y * Width + x] = Math.Clamp(value, -100, 100);
    }
    private bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
}

public sealed class TerrainBrush
{
    public float Radius { get; }
    public float Strength { get; }

    public TerrainBrush(float radius, float strength)
    {
        Guard.Finite(radius, nameof(radius));
        Guard.Finite(strength, nameof(strength));
        if (radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        Radius = radius;
        Strength = strength;
    }

    public void Apply(TerrainMap map, Vec2 p)
    {
        ArgumentNullException.ThrowIfNull(map);
        Guard.Position(p, nameof(p));
        // Double arithmetic avoids overflow for large but finite brush coordinates.
        double left = Math.Max(0, (double)p.X - Radius);
        double right = Math.Min(map.Width - 1, (double)p.X + Radius);
        double top = Math.Max(0, (double)p.Y - Radius);
        double bottom = Math.Min(map.Height - 1, (double)p.Y + Radius);
        if (left > right || top > bottom) return;
        for (int y = (int)Math.Ceiling(top); y <= (int)bottom; y++)
            for (int x = (int)Math.Ceiling(left); x <= (int)right; x++)
            {
                double dx = x - (double)p.X, dy = y - (double)p.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance <= Radius)
                    map.Set(x, y, (float)Math.Clamp(map.Get(x, y) + Strength * (1 - distance / Radius), -100, 100));
            }
    }
}

/// <summary>Bounded physical and behavioral traits; brain weights live on the agent.</summary>
public readonly record struct Genome
{
    public float Speed { get; }
    public float Size { get; }
    public float Vision { get; }
    public float Metabolism { get; }
    public float Aggression { get; }
    public float Sociality { get; }
    public float Curiosity { get; }

    public Genome(float Speed, float Size, float Vision, float Metabolism,
        float Aggression, float Sociality, float Curiosity)
    {
        this.Speed = Bounded(Speed, .1f, 3, nameof(Speed));
        this.Size = Bounded(Size, .25f, 3, nameof(Size));
        this.Vision = Bounded(Vision, 1, 12, nameof(Vision));
        this.Metabolism = Bounded(Metabolism, .1f, 3, nameof(Metabolism));
        this.Aggression = Bounded(Aggression, 0, 1, nameof(Aggression));
        this.Sociality = Bounded(Sociality, 0, 1, nameof(Sociality));
        this.Curiosity = Bounded(Curiosity, 0, 1, nameof(Curiosity));
    }

    private static float Bounded(float value, float min, float max, string name)
    {
        Guard.Finite(value, name);
        return Math.Clamp(value, min, max);
    }

    internal void Validate()
    {
        if (Speed < .1f || Size < .25f || Vision < 1 || Metabolism < .1f)
            throw new ArgumentException("A default/uninitialized genome is not valid.");
    }

    public static Genome Random(Random r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new(.5f + r.NextSingle(), .5f + r.NextSingle(), 2 + r.NextSingle() * 4,
            .5f + r.NextSingle(), r.NextSingle(), r.NextSingle(), r.NextSingle());
    }

    public Genome Mutate(Random r)
    {
        ArgumentNullException.ThrowIfNull(r);
        Validate();
        float Change(float value) => value + (r.NextSingle() - .5f) * .16f;
        return new(Change(Speed), Change(Size), Change(Vision), Change(Metabolism),
            Change(Aggression), Change(Sociality), Change(Curiosity));
    }

    public Genome Crossover(Genome other, Random r)
    {
        ArgumentNullException.ThrowIfNull(r);
        Validate();
        other.Validate();
        float Pick(float a, float b) => r.Next(2) == 0 ? a : b;
        return new(Pick(Speed, other.Speed), Pick(Size, other.Size), Pick(Vision, other.Vision),
            Pick(Metabolism, other.Metabolism), Pick(Aggression, other.Aggression),
            Pick(Sociality, other.Sociality), Pick(Curiosity, other.Curiosity));
    }
}

public sealed class FoodMap
{
    private readonly float[] amounts;
    public int Width { get; }
    public int Height { get; }
    public const float Capacity = 1;

    internal FoodMap(int width, int height, Random random)
    {
        Width = width;
        Height = height;
        amounts = new float[width * height];
        for (int i = 0; i < amounts.Length; i++) amounts[i] = .4f + random.NextSingle() * .6f;
    }

    public float Get(int x, int y) => Contains(x, y) ? amounts[y * Width + x] : 0;
    public void Set(int x, int y, float amount)
    {
        Guard.Finite(amount, nameof(amount));
        if (amount < 0 || amount > Capacity) throw new ArgumentOutOfRangeException(nameof(amount));
        if (!Contains(x, y)) throw new ArgumentOutOfRangeException(nameof(x));
        amounts[y * Width + x] = amount;
    }

    internal float Consume(int x, int y, float requested)
    {
        float eaten = Math.Min(Get(x, y), requested);
        amounts[y * Width + x] -= eaten;
        return eaten;
    }

    internal void Renew(float dt, TerrainMap terrain)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int index = y * Width + x;
                float fertility = 1 / (1 + Math.Abs(terrain.Get(x, y)));
                amounts[index] = Math.Min(Capacity, amounts[index] + .03f * fertility * dt);
            }
    }
    private bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
}

internal enum DeathCause { Other, EnergyDepletion, Fire, Predator, Climate }

/// <summary>Lifetime deaths removed by simulation ticks. External list edits and the Erase tool are not natural deaths.</summary>
public readonly record struct MortalityStatistics(long LifetimeDeaths, long OldAge, long EnergyDepletion,
    long Fire, long Predators, long Climate, long Other);

public sealed class Agent
{
    public const int SensorCount = 12;
    public int Id { get; }
    public Vec2 Position;
    public Genome Genome;
    public float Energy = 1;
    /// <summary>Physical health [0,1]; eating restores energy, not wounds.</summary>
    public float Health { get; internal set; } = 1;
    public float Age { get; internal set; }
    public int Generation { get; internal set; }
    public NeuralNetwork Brain { get; }
    public int SpeciesId { get; internal set; } = -1;
    public int OffspringCount { get; internal set; }
    public Vec2 Heading { get; set; }
    // Reset at each tick; only lethal damage records a cause, never merely nearby exposure.
    internal DeathCause LethalCause { get; set; }

    public Agent(int id, Vec2 p, Genome g) : this(id, p, g, NeuralNetwork.CreateForager(id)) { }

    public Agent(int id, Vec2 p, Genome g, NeuralNetwork brain)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        Guard.Position(p, nameof(p));
        g.Validate();
        ArgumentNullException.ThrowIfNull(brain);
        if (brain.InputCount != SensorCount || brain.OutputCount != 2)
            throw new ArgumentException("Agent brains need twelve inputs and two outputs.", nameof(brain));
        Id = id;
        Position = p;
        Genome = g;
        Brain = brain;
    }
}

/// <summary>Read-only value snapshot of the live movement components. Brain.Network is shared, not cloned.</summary>
public readonly record struct AgentEntitySnapshot(PositionComponent Position, VitalityComponent Vitality,
    GenomeComponent Genome, BrainComponent Brain);

/// <summary>Single-threaded seeded prototype. Each positive Step is one tick; zero is a no-op.</summary>
public sealed partial class Simulation
{
    /// <summary>The default per-world population cap, retained for compatibility.</summary>
    public const int MaximumPopulation = 1000;
    public const int MinimumPopulationLimit = 80;
    public const int AbsoluteMaximumPopulation = 5000;
    /// <summary>Current per-world cap. Lowering it never removes existing agents; births and spawns resume below the cap.</summary>
    public int PopulationLimit { get; private set; } = MaximumPopulation;
    public void SetPopulationLimit(int limit)
    {
        if (limit < MinimumPopulationLimit || limit > AbsoluteMaximumPopulation)
            throw new ArgumentOutOfRangeException(nameof(limit),
                $"Population limit must be between {MinimumPopulationLimit} and {AbsoluteMaximumPopulation}.");
        PopulationLimit = limit;
    }
    public const float BirthCostPerParent = .8f;
    public const float MaximumAge = 90;
    // Procedural hills stay traversable; only deliberately sculpted deep water and peaks block agents.
    public const float DeepWaterLevel = -.6f;
    public const float MountainLevel = 3f;
    public TerrainMap Terrain { get; }
    public FoodMap Food { get; }
    // Retained for App compatibility. External additions are validated before stepping.
    public List<Agent> Agents { get; } = new();
    private readonly AgentStore agentStore = new();
    private readonly EntityRegistry entities = new();
    private readonly Dictionary<int, EntityHandle> entityByAgentId = new();
    // Single-threaded reconciliation and validation never overlap; retain capacity across ticks.
    private readonly HashSet<int> scratchAgentIds = new();
    /// <summary>Number of ECS entities. Reading reconciles the public Agents list first; no mutable registry is exposed.</summary>
    public int EntityCount { get { ValidateAgents(); SynchronizeEntities(); return entities.Count; } }
    /// <summary>Copies current component values for an agent ID after reconciliation. Brain is a shared mutable reference, not a deep copy.</summary>
    public bool TryGetEntitySnapshot(int agentId, out AgentEntitySnapshot snapshot)
    {
        ValidateAgents();
        SynchronizeEntities();
        if (!entityByAgentId.TryGetValue(agentId, out var entity)) { snapshot = default; return false; }
        snapshot = new(entities.Get<PositionComponent>(entity), entities.Get<VitalityComponent>(entity),
            entities.Get<GenomeComponent>(entity), entities.Get<BrainComponent>(entity));
        return true;
    }
    public SimulationDiagnostics Diagnostics => new(Tick, Agents.Count, agentStore.BucketCount, agentStore.CandidateChecks);
    public long Tick { get; private set; }
    /// <summary>Deaths actually removed during Step; the cause is the lethal damage source, with old age taking priority.</summary>
    public MortalityStatistics Mortality { get; private set; }
    public long LifetimeDeaths => Mortality.LifetimeDeaths;
    internal readonly WorldRandom random;
    internal long nextId;
    public int Seed { get; }

    public Simulation(int width = 160, int height = 90, int seed = 42)
    {
        Seed = seed;
        Terrain = new(width, height);
        random = new(seed);
        // Seeded smooth elevation gives a reproducible biome without changing blank TerrainMap semantics.
        double phaseX = random.NextDouble() * Math.PI * 2;
        double phaseY = random.NextDouble() * Math.PI * 2;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double rolling = Math.Sin(x * .065 + phaseX) * Math.Cos(y * .075 + phaseY);
                double detail = Math.Sin((x + y) * .14 + phaseX + phaseY);
                Terrain.Set(x, y, (float)(.25 + .6 * rolling + .15 * detail));
            }
        Food = new(width, height, random);
        for (int i = 0; i < 80; i++)
            Agents.Add(new((int)nextId++, new(random.Next(width), random.Next(height)),
                Genome.Random(random), NeuralNetwork.CreateForager(random.Next()))
            { Heading = RandomHeading() });
        InitializeEnvironment();
        RefreshSpecies();
        SynchronizeEntities();
    }

    public void Step(float dt)
    {
        Guard.Delta(dt);
        if (dt == 0) return;
        using var tickProfile = Profiler?.Measure(SimulationPhase.Tick, Agents.Count);
        using (Profiler?.Measure(SimulationPhase.Validation, Agents.Count)) ValidateAgents();
        using (Profiler?.Measure(SimulationPhase.StateSynchronization, Agents.Count))
            SynchronizeEntities(); // Agent list is authoritative between phases, including external edits.
        Tick = checked(Tick + 1);
        if (ExecuteCommands())
        {
            // Commands may spawn or erase entities after the initial reconciliation.
            using (Profiler?.Measure(SimulationPhase.StateSynchronization, Agents.Count)) SynchronizeEntities();
        }
        foreach (var agent in Agents) agent.LethalCause = DeathCause.Other;
        using (Profiler?.Measure(SimulationPhase.Ecology, Environment.Width * Environment.Height))
        {
            Food.Renew(dt, Terrain);
            StepEnvironment(dt);
        }
        using (Profiler?.Measure(SimulationPhase.StateSynchronization, Agents.Count))
            SynchronizeEntities(); // Environmental hazards and predators edit Agent energy.
        Span<float> inputs = stackalloc float[Agent.SensorCount];
        Span<float> output = stackalloc float[2];
        Span<float> scratch = stackalloc float[Agent.SensorCount + 2 + 128];
        using (Profiler?.Measure(SimulationPhase.LocalBehavior, Agents.Count))
        foreach (var agent in Agents)
        {
            // Preserve public list order (food competition and replay), but perform movement against
            // registry components by stable ID/handle. No structural ECS edits occur in this loop.
            var entity = entityByAgentId[agent.Id];
            ref var position = ref entities.Get<PositionComponent>(entity);
            ref var vitality = ref entities.Get<VitalityComponent>(entity);
            if (agent.Health <= 0 || vitality.Energy <= 0 || agent.Age >= MaximumAge) continue;
            var genome = entities.Get<GenomeComponent>(entity).Value;
            var brain = entities.Get<BrainComponent>(entity).Network;
            agent.Age += dt;
            Sense(agent, inputs);
            // Public sensor slots preserve their original meaning; the controller receives centered levels.
            for (int i = 0; i < 4; i++) inputs[i] -= .5f;
            inputs[8] = inputs[8] * 2 - 1;
            inputs[9] = inputs[9] * 2 - 1;
            brain.Evaluate(inputs, output, scratch);
            var steering = new Vec2(output[0], output[1]);
            float persistence = Math.Clamp(1 - dt * 1.4f, 0, 1);
            var motion = agent.Heading == default ? steering : agent.Heading * persistence + steering * (1 - persistence);
            if (TryConstructionMotion(agent, dt, out var buildMotion)) motion = buildMotion;
            else if (TryHaulMotion(agent, dt, out var haulMotion)) motion = haulMotion;
            else if (TryMealMotion(agent, dt, out var mealMotion)) motion = mealMotion;
            float length = MathF.Sqrt(motion.X * motion.X + motion.Y * motion.Y);
            if (length > 1) motion *= 1 / length;
            // Preserve the original terrain drag, but sweep the slowed motion through every intervening
            // cell. Reflect individual velocity components at walls or blocked cells, without teleporting.
            var target = position.Value + motion * (genome.Speed * 4 * dt);
            float previewX = motion.X, previewY = motion.Y;
            target = new(Reflect(target.X, Terrain.Width - 1, ref previewX),
                Reflect(target.Y, Terrain.Height - 1, ref previewY));
            float elevation = Terrain.Get((int)position.Value.X, (int)position.Value.Y);
            float slope = Math.Abs(Terrain.Get((int)target.X, (int)target.Y) - elevation);
            var moved = SweepMovement(position.Value, motion, genome.Speed * 4 * dt / (1 + slope), out var heading);
            agent.Heading = heading;
            position = new(moved);
            float eaten = Food.Consume((int)moved.X, (int)moved.Y, .35f * genome.Size * dt);
            vitality = vitality with { Energy = Math.Min(3, vitality.Energy + eaten - dt *
                (.12f * genome.Metabolism * genome.Size + .025f * genome.Speed * length * (1 + slope))) };
            // Explicit write-back: subsequent civilization/reproduction and public observers read Agents.
            agent.Position = position.Value;
            agent.Energy = vitality.Energy;
            if (agent.Energy <= 0) agent.LethalCause = DeathCause.EnergyDepletion;
        }
        using (Profiler?.Measure(SimulationPhase.DeathRemoval, Agents.Count))
            RemoveDeadAgents();
        StepHauling();
        using (Profiler?.Measure(SimulationPhase.Reproduction, Agents.Count)) Reproduce(dt);
        using (Profiler?.Measure(SimulationPhase.Civilization, Agents.Count)) StepCivilization(dt);
        using (Profiler?.Measure(SimulationPhase.DeathRemoval, Agents.Count)) RemoveDeadAgents();
        StepHauling(cleanupOnly: true);
        StepConstruction(dt);
        CleanMealJourneys();
        using (Profiler?.Measure(SimulationPhase.StateSynchronization, Agents.Count))
            SynchronizeEntities(); // Account for deaths, newborns and civilization energy edits.
    }

    /// <summary>Food E/W/S/N (0 outside), terrain E/W/S/N relative heights (1 for walls or impassable terrain), energy, local food, curiosity oscillator, bias. Level inputs are centered before brain evaluation.</summary>
    public float[] Sense(Agent agent)
    {
        var inputs = new float[Agent.SensorCount];
        Sense(agent, inputs);
        return inputs;
    }

    public void Sense(Agent agent, Span<float> inputs)
    {
        if (inputs.Length < Agent.SensorCount) throw new ArgumentException("Sensor buffer is too small.", nameof(inputs));
        ArgumentNullException.ThrowIfNull(agent);
        Guard.Position(agent.Position, nameof(agent));
        agent.Genome.Validate();
        Guard.Finite(agent.Energy, nameof(agent));
        Vec2 p = Clamp(agent.Position);
        int x = (int)p.X, y = (int)p.Y;
        int distance = Math.Max(1, (int)agent.Genome.Vision);
        ReadOnlySpan<(int X, int Y)> directions = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        for (int i = 0; i < directions.Length; i++)
        {
            int sx = x + directions[i].X * distance;
            int sy = y + directions[i].Y * distance;
            bool outside = sx < 0 || sy < 0 || sx >= Terrain.Width || sy >= Terrain.Height;
            bool blocked = outside;
            for (int step = 1; step <= distance && !blocked; step++)
                blocked = IsImpassable(x + directions[i].X * step, y + directions[i].Y * step);
            inputs[i] = Food.Get(sx, sy);
            inputs[i + 4] = blocked ? 1 : MathF.Tanh(Terrain.Get(sx, sy) - Terrain.Get(x, y));
        }
        inputs[8] = Math.Clamp(agent.Energy / 3, 0, 1);
        inputs[9] = Food.Get(x, y);
        inputs[10] = (float)Math.Sin(agent.Age + agent.Id) * agent.Genome.Curiosity;
        inputs[11] = 1;
    }

    // Public Agents is the inter-phase authority. Match IDs rather than list indexes because callers
    // may reorder, remove, replace or append agents; stale registry handles are destroyed before reuse.
    private void SynchronizeEntities()
    {
        var currentIds = scratchAgentIds;
        currentIds.Clear();
        foreach (var agent in Agents) currentIds.Add(agent.Id);
        // Dictionary.Remove is supported during enumeration on .NET 8; avoid a per-phase snapshot.
        foreach (var pair in entityByAgentId)
            if (!currentIds.Contains(pair.Key))
            {
                entities.Destroy(pair.Value);
                entityByAgentId.Remove(pair.Key);
            }
        foreach (var agent in Agents)
        {
            if (!entityByAgentId.TryGetValue(agent.Id, out var entity))
            {
                entity = entities.Create();
                entityByAgentId.Add(agent.Id, entity);
                entities.Add(entity, new PositionComponent(agent.Position));
                entities.Add(entity, new VitalityComponent(Energy: agent.Energy, Health: agent.Health));
                entities.Add(entity, new GenomeComponent(agent.Genome));
                entities.Add(entity, new BrainComponent(agent.Brain));
            }
            else
            {
                entities.Get<PositionComponent>(entity) = new(agent.Position);
                entities.Get<VitalityComponent>(entity) = new(Energy: agent.Energy, Health: agent.Health);
                entities.Get<GenomeComponent>(entity) = new(agent.Genome);
                entities.Get<BrainComponent>(entity) = new(agent.Brain);
            }
        }
    }

    private void ValidateAgents()
    {
        var ids = scratchAgentIds;
        ids.Clear();
        foreach (var agent in Agents)
        {
            if (agent is null) throw new InvalidOperationException("Agent collection contains null.");
            if (!ids.Add(agent.Id)) throw new InvalidOperationException("Agent IDs must be unique.");
            Guard.Position(agent.Position, nameof(Agents));
            Guard.Position(agent.Heading, nameof(Agents));
            Guard.Finite(agent.Age, nameof(Agents));
            Guard.Finite(agent.Energy, nameof(Agents));
            if (!float.IsFinite(agent.Health) || agent.Health < 0 || agent.Health > 1)
                throw new ArgumentOutOfRangeException(nameof(Agents), "Invalid health.");
            agent.Genome.Validate();
            if (agent.Position != Clamp(agent.Position)) throw new ArgumentOutOfRangeException(nameof(Agents), "Agent is outside the map.");
            nextId = Math.Max(nextId, (long)agent.Id + 1);
        }
    }

    private void Reproduce(float dt)
    {
        RefreshSpecies();
        if (Agents.Count >= PopulationLimit || nextId > int.MaxValue) return;
        var sizes = Species.ToDictionary(s => s.Id, s => s.MemberCount);
        // Online fitness sharing prioritizes rare compatible groups when local mates/capacity are scarce.
        // This is steady-state ecological selection, not generations, species quotas or full NEAT.
        var parents = Agents.Where(a => a.Health > 0 && a.Age >= 5 && a.Energy >= 1.7f)
            .OrderByDescending(a => ReproductiveFitness(a) / sizes[a.SpeciesId])
            .ThenBy(a => a.Id).ToArray();
        var used = new HashSet<int>();
        foreach (var parent in parents)
        {
            if (Agents.Count >= PopulationLimit || nextId > int.MaxValue) break;
            if (used.Contains(parent.Id)) continue;
            bool Available(Agent a) => a.Id != parent.Id && !used.Contains(a.Id) &&
                Math.Abs(a.Position.X - parent.Position.X) <= 3 && Math.Abs(a.Position.Y - parent.Position.Y) <= 3;
            var mate = parents.FirstOrDefault(a => a.SpeciesId == parent.SpeciesId && Available(a));
            if (mate is null && AllowInterspeciesMating) mate = parents.FirstOrDefault(Available);
            if (mate is null) continue;
            // Territorial crowding reduces successful matings smoothly; it is not a soft population cap.
            // Competition for sites lowers birth success at high density, but never directly kills an agent.
            float crowding = (float)Agents.Count / PopulationLimit;
            float freeSpace = 1 - crowding;
            float birthRate = .32f * freeSpace * freeSpace * freeSpace;
            if (random.NextSingle() >= 1 - MathF.Exp(-birthRate * dt)) continue;
            var genome = parent.Genome.Crossover(mate.Genome, random).Mutate(random);
            var brain = parent.Brain.Crossover(mate.Brain, random);
            var child = new Agent((int)nextId++, Clamp(parent.Position + new Vec2(random.NextSingle() - .5f,
                random.NextSingle() - .5f)), genome, brain)
            {
                Generation = Math.Max(parent.Generation, mate.Generation) + 1,
                Heading = RandomHeading()
            };
            // Combined cost exceeds newborn energy: reproduction never creates free energy.
            parent.Energy -= BirthCostPerParent;
            mate.Energy -= BirthCostPerParent;
            if (parent.OffspringCount < int.MaxValue) parent.OffspringCount++;
            if (mate.OffspringCount < int.MaxValue) mate.OffspringCount++;
            used.Add(parent.Id);
            used.Add(mate.Id);
            Agents.Add(child);
            // Membership is cultural, not a sociality gene: a newborn continues its family settlement.
            var family = tribes.FirstOrDefault(t => t.MemberIds.Contains(parent.Id))
                ?? tribes.FirstOrDefault(t => t.MemberIds.Contains(mate.Id));
            family?.MemberIds.Add(child.Id);
        }
        RefreshSpecies();
    }

    partial void InitializeEnvironment();
    partial void StepEnvironment(float dt);
    partial void StepCivilization(float dt);

    public Agent SpawnAgent(Vec2 position)
    {
        Guard.Position(position, nameof(position));
        if (position != Clamp(position)) throw new ArgumentOutOfRangeException(nameof(position));
        ValidateAgents();
        if (Agents.Count >= PopulationLimit || nextId > int.MaxValue) throw new InvalidOperationException("Population or ID capacity reached.");
        var agent = new Agent((int)nextId++, position, Genome.Random(random), NeuralNetwork.CreateForager(random.Next())) { Heading = RandomHeading() };
        Agents.Add(agent);
        RefreshSpecies();
        return agent;
    }

    private Vec2 RandomHeading()
    {
        float angle = random.NextSingle() * MathF.Tau;
        return new(MathF.Cos(angle) * .65f, MathF.Sin(angle) * .65f);
    }
    private bool IsImpassable(int x, int y)
    {
        float height = Terrain.Get(x, y);
        return height <= DeepWaterLevel || height >= MountainLevel;
    }

    private Vec2 SweepMovement(Vec2 start, Vec2 velocity, float distance, out Vec2 heading)
    {
        // At most a quarter cell per sweep: even maximum speed and dt cannot skip a thin obstacle.
        int steps = Math.Max(1, (int)Math.Ceiling(
            Math.Max(Math.Abs((double)velocity.X), Math.Abs((double)velocity.Y)) * distance * 4));
        float dx = velocity.X * (distance / steps), dy = velocity.Y * (distance / steps);
        float x = start.X, y = start.Y;
        for (int step = 0; step < steps; step++)
        {
            float nx = Reflect(x + dx, Terrain.Width - 1, ref dx);
            float ny = Reflect(y + dy, Terrain.Height - 1, ref dy);
            if ((int)nx != (int)x && IsImpassable((int)nx, (int)y))
            {
                // Mirror about the shared cell edge, rather than clipping at an arbitrary distance.
                float edge = dx > 0 ? (int)nx : (int)x;
                nx = dx > 0 ? MathF.BitDecrement(2 * edge - nx) : 2 * edge - nx;
                dx = -dx;
            }
            if ((int)ny != (int)y && IsImpassable((int)x, (int)ny))
            {
                float edge = dy > 0 ? (int)ny : (int)y;
                ny = dy > 0 ? MathF.BitDecrement(2 * edge - ny) : 2 * edge - ny;
                dy = -dy;
            }
            // A diagonal crossing into a blocked cell must also be stopped when both side cells are clear.
            if (IsImpassable((int)nx, (int)ny) && !IsImpassable((int)x, (int)y))
            {
                nx = x;
                ny = y;
                dx = -dx;
                dy = -dy;
            }
            x = nx;
            y = ny;
        }
        // Wall and obstacle reflections change signs but retain the original heading magnitude.
        heading = new(MathF.CopySign(Math.Abs(velocity.X), dx), MathF.CopySign(Math.Abs(velocity.Y), dy));
        return new(x, y);
    }

    private static float Reflect(float coordinate, float maximum, ref float direction)
    {
        if (maximum <= 0) { direction = 0; return 0; }
        while (coordinate < 0 || coordinate > maximum)
        {
            if (coordinate < 0) { coordinate = -coordinate; direction = Math.Abs(direction); }
            if (coordinate > maximum) { coordinate = 2 * maximum - coordinate; direction = -Math.Abs(direction); }
        }
        return coordinate;
    }
    private Vec2 Clamp(Vec2 p) => new(Math.Clamp(p.X, 0, Terrain.Width - 1), Math.Clamp(p.Y, 0, Terrain.Height - 1));
}

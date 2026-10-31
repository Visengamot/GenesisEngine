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

public sealed class Agent
{
    public const int SensorCount = 12;
    public int Id { get; }
    public Vec2 Position;
    public Genome Genome;
    public float Energy = 1;
    public float Age { get; internal set; }
    public int Generation { get; internal set; }
    public NeuralNetwork Brain { get; }
    public int SpeciesId { get; internal set; } = -1;
    public int OffspringCount { get; internal set; }
    public Vec2 Heading { get; set; }

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

/// <summary>Single-threaded seeded prototype. Each positive Step is one tick; zero is a no-op.</summary>
public sealed partial class Simulation
{
    public const int MaximumPopulation = 600;
    public const float BirthCostPerParent = .65f;
    public const float MaximumAge = 180;
    public TerrainMap Terrain { get; }
    public FoodMap Food { get; }
    // Retained for App compatibility. External additions are validated before stepping.
    public List<Agent> Agents { get; } = new();
    private readonly AgentStore agentStore = new();
    public SimulationDiagnostics Diagnostics => new(Tick, Agents.Count, agentStore.BucketCount, agentStore.CandidateChecks);
    public long Tick { get; private set; }
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
    }

    public void Step(float dt)
    {
        Guard.Delta(dt);
        if (dt == 0) return;
        ValidateAgents();
        Tick = checked(Tick + 1);
        Food.Renew(dt, Terrain);
        StepEnvironment(dt);
        Span<float> inputs = stackalloc float[Agent.SensorCount];
        Span<float> output = stackalloc float[2];
        Span<float> scratch = stackalloc float[Agent.SensorCount + 2 + 128];
        foreach (var agent in Agents)
        {
            if (agent.Energy <= 0 || agent.Age >= MaximumAge) continue;
            agent.Age += dt;
            Sense(agent, inputs);
            // Public sensor slots preserve their original meaning; the controller receives centered levels.
            for (int i = 0; i < 4; i++) inputs[i] -= .5f;
            inputs[8] = inputs[8] * 2 - 1;
            inputs[9] = inputs[9] * 2 - 1;
            agent.Brain.Evaluate(inputs, output, scratch);
            var steering = new Vec2(output[0], output[1]);
            float persistence = Math.Clamp(1 - dt * 1.4f, 0, 1);
            var motion = agent.Heading == default ? steering : agent.Heading * persistence + steering * (1 - persistence);
            float length = MathF.Sqrt(motion.X * motion.X + motion.Y * motion.Y);
            if (length > 1) motion *= 1 / length;
            // Reflect at real walls, retaining the reflected heading. No teleportation or center attraction.
            var target = agent.Position + motion * (agent.Genome.Speed * 4 * dt);
            float mx = motion.X, my = motion.Y;
            target = new(Reflect(target.X, Terrain.Width - 1, ref mx), Reflect(target.Y, Terrain.Height - 1, ref my));
            agent.Heading = new(mx, my);
            float elevation = Terrain.Get((int)agent.Position.X, (int)agent.Position.Y);
            float slope = Math.Abs(Terrain.Get((int)target.X, (int)target.Y) - elevation);
            agent.Position = Clamp(agent.Position + new Vec2(target.X - agent.Position.X,
                target.Y - agent.Position.Y) * (1 / (1 + slope)));
            float eaten = Food.Consume((int)agent.Position.X, (int)agent.Position.Y, .35f * agent.Genome.Size * dt);
            agent.Energy = Math.Min(3, agent.Energy + eaten - dt *
                (.06f * agent.Genome.Metabolism * agent.Genome.Size + .025f * agent.Genome.Speed * length * (1 + slope)));
        }
        Agents.RemoveAll(a => a.Energy <= 0 || a.Age >= MaximumAge);
        Reproduce();
        StepCivilization(dt);
    }

    /// <summary>Food E/W/S/N (0 outside), terrain E/W/S/N relative heights (1 for walls), energy, local food, curiosity oscillator, bias. Level inputs are centered before brain evaluation.</summary>
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
            inputs[i] = Food.Get(sx, sy);
            inputs[i + 4] = outside ? 1 : MathF.Tanh(Terrain.Get(sx, sy) - Terrain.Get(x, y));
        }
        inputs[8] = Math.Clamp(agent.Energy / 3, 0, 1);
        inputs[9] = Food.Get(x, y);
        inputs[10] = (float)Math.Sin(agent.Age + agent.Id) * agent.Genome.Curiosity;
        inputs[11] = 1;
    }

    private void ValidateAgents()
    {
        var ids = new HashSet<int>();
        foreach (var agent in Agents)
        {
            if (agent is null) throw new InvalidOperationException("Agent collection contains null.");
            if (!ids.Add(agent.Id)) throw new InvalidOperationException("Agent IDs must be unique.");
            Guard.Position(agent.Position, nameof(Agents));
            Guard.Position(agent.Heading, nameof(Agents));
            Guard.Finite(agent.Age, nameof(Agents));
            Guard.Finite(agent.Energy, nameof(Agents));
            agent.Genome.Validate();
            if (agent.Position != Clamp(agent.Position)) throw new ArgumentOutOfRangeException(nameof(Agents), "Agent is outside the map.");
            nextId = Math.Max(nextId, (long)agent.Id + 1);
        }
    }

    private void Reproduce()
    {
        RefreshSpecies();
        var sizes = Species.ToDictionary(s => s.Id, s => s.MemberCount);
        // Online fitness sharing prioritizes rare compatible groups when local mates/capacity are scarce.
        // This is steady-state ecological selection, not generations, species quotas or full NEAT.
        var parents = Agents.Where(a => a.Age >= 5 && a.Energy >= 1.5f)
            .OrderByDescending(a => ReproductiveFitness(a) / sizes[a.SpeciesId])
            .ThenBy(a => a.Id).ToArray();
        var used = new HashSet<int>();
        foreach (var parent in parents)
        {
            if (Agents.Count >= MaximumPopulation || nextId > int.MaxValue) break;
            if (used.Contains(parent.Id)) continue;
            bool Available(Agent a) => a.Id != parent.Id && !used.Contains(a.Id) &&
                Math.Abs(a.Position.X - parent.Position.X) <= 3 && Math.Abs(a.Position.Y - parent.Position.Y) <= 3;
            var mate = parents.FirstOrDefault(a => a.SpeciesId == parent.SpeciesId && Available(a));
            if (mate is null && AllowInterspeciesMating) mate = parents.FirstOrDefault(Available);
            if (mate is null) continue;
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
        if (Agents.Count >= MaximumPopulation || nextId > int.MaxValue) throw new InvalidOperationException("Population or ID capacity reached.");
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

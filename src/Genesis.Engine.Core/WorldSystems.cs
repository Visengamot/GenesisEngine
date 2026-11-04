namespace Genesis.Engine.Core;

public enum GodTool { Inspect, Raise, Lower, Water, Dry, Heat, Cool, Forest, Food, Stone, Fire, Predator, People, Erase }

/// <summary>Normalized resources [0,1]; temperature is Celsius [-50,60]. Out-of-map reads return default.</summary>
public readonly record struct EnvironmentCell(float Moisture, float Temperature, float Vegetation, float Wood, float Stone, float Fire);

public sealed class EnvironmentGrid
{
    private readonly EnvironmentCell[] cells;
    public int Width { get; }
    public int Height { get; }
    internal EnvironmentGrid(TerrainMap terrain)
    {
        Width = terrain.Width; Height = terrain.Height;
        cells = new EnvironmentCell[Width * Height];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float h = terrain.Get(x, y);
                float moisture = Math.Clamp(.65f - h * .25f, 0, 1);
                float vegetation = Math.Clamp(moisture * (1 - Math.Abs(h) * .3f), 0, 1);
                Set(x, y, new(moisture, 22 - h * 8, vegetation, vegetation * .7f, Math.Clamp(.2f + Math.Abs(h) * .3f, 0, 1), 0));
            }
    }
    public EnvironmentCell Get(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height ? cells[y * Width + x] : default;
    internal void Set(int x, int y, EnvironmentCell c) => cells[y * Width + x] = new(
        Math.Clamp(c.Moisture, 0, 1), Math.Clamp(c.Temperature, -50, 60), Math.Clamp(c.Vegetation, 0, 1),
        Math.Clamp(c.Wood, 0, 1), Math.Clamp(c.Stone, 0, 1), Math.Clamp(c.Fire, 0, 1));
    public static float Fertility(EnvironmentCell c) => c.Moisture * Math.Clamp(1 - Math.Abs(c.Temperature - 22) / 35, 0, 1) * (1 - c.Fire);
}

public sealed class Predator
{
    public Vec2 Position { get; internal set; }
    public float Energy { get; internal set; } = 1;
    internal Predator(Vec2 position) => Position = position;
}

public sealed partial class Simulation
{
    public EnvironmentGrid Environment { get; private set; } = null!;
    private readonly List<Predator> predators = new();
    public IReadOnlyList<Predator> Predators => predators.AsReadOnly();

    partial void InitializeEnvironment() => Environment = new(Terrain);

    /// <summary>Applies a circular, falloff brush. Radius must be positive, strength nonnegative and finite.
    /// Strength is capped at 100; entity spawns are capped and occur once at the brush center. Outside centers do not spawn.</summary>
    public void ApplyTool(GodTool tool, Vec2 position, float radius, float strength)
    {
        if (!Enum.IsDefined(tool)) throw new ArgumentOutOfRangeException(nameof(tool));
        Guard.Position(position, nameof(position)); Guard.Finite(radius, nameof(radius)); Guard.Finite(strength, nameof(strength));
        if (radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (strength < 0) throw new ArgumentOutOfRangeException(nameof(strength));
        strength = Math.Min(strength, 100);
        if (strength == 0 || tool == GodTool.Inspect) return;
        if (tool is GodTool.People or GodTool.Predator)
        {
            if (position != Clamp(position)) return;
            if (tool == GodTool.Predator && predators.Count < 64) predators.Add(new(position));
            if (tool == GodTool.People && Agents.Count < PopulationLimit)
            {
                ValidateAgents();
                if (nextId <= int.MaxValue) SpawnAgent(position);
            }
            return;
        }
        double left = Math.Max(0, (double)position.X - radius), right = Math.Min(Terrain.Width - 1, (double)position.X + radius);
        double top = Math.Max(0, (double)position.Y - radius), bottom = Math.Min(Terrain.Height - 1, (double)position.Y + radius);
        if (left > right || top > bottom) return;
        for (int y = (int)Math.Ceiling(top); y <= (int)bottom; y++)
            for (int x = (int)Math.Ceiling(left); x <= (int)right; x++)
            {
                double dx = x - (double)position.X, dy = y - (double)position.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > radius) continue;
                float amount = strength * (float)(1 - distance / radius);
                var c = Environment.Get(x, y);
                switch (tool)
                {
                    case GodTool.Raise: Terrain.Set(x, y, Terrain.Get(x, y) + amount); break;
                    case GodTool.Lower: Terrain.Set(x, y, Terrain.Get(x, y) - amount); break;
                    case GodTool.Water: c = c with { Moisture = c.Moisture + amount }; Terrain.Set(x, y, Math.Min(Terrain.Get(x, y), Terrain.Get(x, y) + (-.25f - Terrain.Get(x, y)) * Math.Min(1, amount))); break;
                    case GodTool.Dry: c = c with { Moisture = c.Moisture - amount }; if (Terrain.Get(x, y) < 0) Terrain.Set(x, y, Terrain.Get(x, y) + (.05f - Terrain.Get(x, y)) * Math.Min(1, amount)); break;
                    case GodTool.Heat: c = c with { Temperature = c.Temperature + amount * 10 }; break;
                    case GodTool.Cool: c = c with { Temperature = c.Temperature - amount * 10 }; break;
                    case GodTool.Forest: c = c with { Vegetation = c.Vegetation + amount, Wood = c.Wood + amount }; break;
                    case GodTool.Food: Food.Set(x, y, Math.Clamp(Food.Get(x, y) + amount, 0, 1)); break;
                    case GodTool.Stone: c = c with { Stone = c.Stone + amount }; break;
                    case GodTool.Fire: c = c with { Fire = c.Fire + amount }; break;
                    case GodTool.Erase:
                        c = c with { Vegetation = 0, Wood = 0, Stone = 0, Fire = 0 }; Food.Set(x, y, 0); break;
                }
                Environment.Set(x, y, c);
            }
        if (tool == GodTool.Erase)
        {
            bool Inside(Vec2 p) { double dx = (double)p.X - position.X, dy = (double)p.Y - position.Y; return dx * dx + dy * dy <= (double)radius * radius; }
            Agents.RemoveAll(a => Inside(a.Position)); predators.RemoveAll(p => Inside(p.Position)); structures.RemoveAll(s => Inside(s.Position));
        }
    }

    private float[]? fireSnapshot;
    partial void StepEnvironment(float dt)
    {
        fireSnapshot ??= new float[Environment.Width * Environment.Height];
        for (int y = 0; y < Environment.Height; y++)
            for (int x = 0; x < Environment.Width; x++) fireSnapshot[y * Environment.Width + x] = Environment.Get(x, y).Fire;
        float NeighborFire(int x, int y) => x < 0 || y < 0 || x >= Environment.Width || y >= Environment.Height ? 0 : fireSnapshot[y * Environment.Width + x];
        for (int y = 0; y < Environment.Height; y++)
            for (int x = 0; x < Environment.Width; x++)
            {
                var c = Environment.Get(x, y);
                float fertility = EnvironmentGrid.Fertility(c);
                float burning = c.Fire * dt;
                // Synchronous four-neighbor fuel-limited spread, independent of scan direction.
                float adjacent = NeighborFire(x - 1, y) + NeighborFire(x + 1, y) + NeighborFire(x, y - 1) + NeighborFire(x, y + 1);
                float fire = c.Fire + dt * (adjacent * .12f * c.Wood * (1 - c.Moisture) + c.Fire * c.Wood * (1 - c.Moisture) * .3f - c.Fire * (.2f + c.Moisture));
                if (c.Wood < .01f) fire -= .5f * dt;
                // Replenishment balances evaporation at the terrain's normal climate.
                // Previously even 22 C lost .0014 moisture/second forever: every plain became desert.
                float elevation = Terrain.Get(x, y);
                float normalTemperature = 22 - elevation * 8;
                float normalMoisture = Math.Clamp(.65f - elevation * .25f, 0, 1);
                float excessHeat = Math.Max(0, c.Temperature - normalTemperature);
                c = c with
                {
                    Moisture = c.Moisture + dt * ((normalMoisture - c.Moisture) * .01f - excessHeat * .0002f - c.Fire * .04f),
                    Temperature = c.Temperature + dt * ((22 - Terrain.Get(x, y) * 8 - c.Temperature) * .01f + c.Fire * 4),
                    Vegetation = c.Vegetation + dt * fertility * .015f - burning * .4f,
                    Wood = c.Wood + dt * fertility * .005f - burning * .3f,
                    Fire = fire
                };
                Environment.Set(x, y, c);
                Food.Set(x, y, Math.Clamp(Food.Get(x, y) + dt * (.025f * fertility - .02f * (1 - fertility)) - burning * .5f, 0, 1));
            }
        foreach (var a in Agents)
        {
            var c = Environment.Get((int)a.Position.X, (int)a.Position.Y);
            float fireDamage = dt * c.Fire * .7f;
            float climateDamage = dt * Math.Max(0, Math.Abs(c.Temperature - 22) - 15) * .0015f;
            float before = a.Energy;
            a.Energy = Math.Clamp(before - fireDamage - climateDamage, 0, 3);
            if (before > 0 && a.Energy <= 0)
                a.LethalCause = fireDamage >= climateDamage ? DeathCause.Fire : DeathCause.Climate;
        }
        // Reconcile public mutable Agents at the phase boundary; predicates read live energy
        // so a prey killed by an earlier predator is not selected again.
        if (predators.Count > 0)
            using (Profiler?.Measure(SimulationPhase.SpatialIndex, Agents.Count)) agentStore.Rebuild(Agents);
        using (Profiler?.Measure(SimulationPhase.PredatorBehavior, predators.Count))
        {
        foreach (var p in predators)
        {
            var prey = agentStore.Nearest(p.Position, a => a.Energy > 0);
            p.Energy = Math.Max(0, p.Energy - dt * .025f);
            if (prey is null) continue;
            float distance = MathF.Sqrt(DistanceSquared(prey.Position, p.Position));
            if (distance > .5f) p.Position = Clamp(p.Position + new Vec2(prey.Position.X - p.Position.X, prey.Position.Y - p.Position.Y) * (Math.Min(distance, dt * 2.5f) / distance));
            if (DistanceSquared(prey.Position, p.Position) < 1)
            {
                float damage = Math.Min(prey.Energy, dt * .35f);
                prey.Energy -= damage;
                if (prey.Energy <= 0 && damage > 0) prey.LethalCause = DeathCause.Predator;
                p.Energy = Math.Min(3, p.Energy + damage * .6f);
            }
        }
        predators.RemoveAll(p => p.Energy <= 0);
        }
    }
    private static float DistanceSquared(Vec2 a, Vec2 b) { float dx = a.X - b.X, dy = a.Y - b.Y; return dx * dx + dy * dy; }
}

namespace Genesis.Engine.Core;

/// <summary>An agent identity, not an archetype or generation-based ECS handle.</summary>
public readonly record struct EntityId(int Value);

/// <summary>Lightweight spatial buckets over existing Agent objects. Rebuild after membership or position changes.
/// Energy and other non-position fields are read live. Ties preserve source-list order.</summary>
public sealed class AgentStore
{
    private readonly float cellSize;
    private readonly Dictionary<(double X, double Y), List<(Agent Agent, int Order)>> buckets = new();
    public int Count { get; private set; }
    public int BucketCount => buckets.Count;
    public long CandidateChecks { get; private set; }
    public AgentStore(float cellSize = 4)
    {
        if (!float.IsFinite(cellSize) || cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
        this.cellSize = cellSize;
    }
    public void Rebuild(IEnumerable<Agent> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        buckets.Clear(); Count = 0; CandidateChecks = 0;
        var ids = new HashSet<EntityId>();
        foreach (var agent in agents)
        {
            ArgumentNullException.ThrowIfNull(agent);
            Guard.Position(agent.Position, nameof(agents));
            if (!ids.Add(new(agent.Id))) throw new InvalidOperationException("Agent IDs must be unique.");
            var key = (Math.Floor((double)agent.Position.X / cellSize), Math.Floor((double)agent.Position.Y / cellSize));
            if (!buckets.TryGetValue(key, out var bucket)) buckets[key] = bucket = new();
            bucket.Add((agent, Count++));
        }
    }
    public Agent? Nearest(Vec2 position, Func<Agent, bool>? filter = null)
    {
        Guard.Position(position, nameof(position));
        Agent? best = null; double distance = double.PositiveInfinity; int order = int.MaxValue;
        // Sort occupied cells by their exact lower bound; empty map area costs nothing.
        foreach (var entry in buckets.Select(b => (Bucket: b.Value, Bound: LowerBound(b.Key, position))).OrderBy(b => b.Bound))
        {
            if (entry.Bound > distance) break;
            foreach (var item in entry.Bucket)
            {
                CandidateChecks++;
                if (filter is not null && !filter(item.Agent)) continue;
                double d = DistanceSquared(item.Agent.Position, position);
                if (d < distance || (d == distance && item.Order < order))
                { best = item.Agent; distance = d; order = item.Order; }
            }
        }
        return best;
    }
    /// <summary>Square neighborhood, returned in rebuild order (suitable for existing mating rules).</summary>
    public IReadOnlyList<Agent> QueryBox(Vec2 position, float halfExtent)
    {
        Guard.Position(position, nameof(position));
        if (!float.IsFinite(halfExtent) || halfExtent < 0) throw new ArgumentOutOfRangeException(nameof(halfExtent));
        var matches = new List<(Agent Agent, int Order)>();
        foreach (var bucket in buckets)
        {
            double left = bucket.Key.X * cellSize, top = bucket.Key.Y * cellSize;
            if (left > (double)position.X + halfExtent || left + cellSize < (double)position.X - halfExtent ||
                top > (double)position.Y + halfExtent || top + cellSize < (double)position.Y - halfExtent) continue;
            foreach (var item in bucket.Value)
            {
                CandidateChecks++;
                if (Math.Abs((double)item.Agent.Position.X - position.X) <= halfExtent && Math.Abs((double)item.Agent.Position.Y - position.Y) <= halfExtent) matches.Add(item);
            }
        }
        matches.Sort((a, b) => a.Order.CompareTo(b.Order));
        return matches.Select(a => a.Agent).ToArray();
    }
    private double LowerBound((double X, double Y) key, Vec2 p)
    {
        double x = key.X * cellSize, y = key.Y * cellSize;
        double dx = Math.Max(0, Math.Max(x - p.X, p.X - (x + cellSize)));
        double dy = Math.Max(0, Math.Max(y - p.Y, p.Y - (y + cellSize)));
        return dx * dx + dy * dy;
    }
    private static double DistanceSquared(Vec2 a, Vec2 b)
    { double dx = (double)a.X - b.X, dy = (double)a.Y - b.Y; return dx * dx + dy * dy; }
}

/// <summary>Last predator-phase index statistics; not a full profiler.</summary>
public readonly record struct SimulationDiagnostics(long Tick, int AgentCount, int SpatialBucketCount, long SpatialCandidateChecks);

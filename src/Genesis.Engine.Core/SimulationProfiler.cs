using System.Diagnostics;

namespace Genesis.Engine.Core;

/// <summary>Diagnostic phases, not independent schedulers. Nested timings must not be added together.</summary>
public enum SimulationPhase
{
    Tick, Validation, StateSynchronization, Ecology, LocalBehavior,
    DeathRemoval, Reproduction, SpeciesClassification, Civilization,
    SpatialIndex, PredatorBehavior,
    Hauling, HaulAssignment, Construction, MealAssignment, MealService, MealCleanup
}

public sealed record PhaseProfile(SimulationPhase Phase, int RetainedSamples, long TotalSamples,
    double MeanMilliseconds, double P50Milliseconds, double P95Milliseconds,
    double P99Milliseconds, double MaximumMilliseconds, double MeanAllocatedBytes,
    long ProcessedEntities);

public sealed record SimulationProfileSnapshot(IReadOnlyList<PhaseProfile> Phases,
    int Gen0Collections, int Gen1Collections, int Gen2Collections);

/// <summary>
/// Opt-in, bounded diagnostics for the current single-threaded simulation. Allocation counts are
/// calling-thread counts, not a promise of complete accounting for future parallel systems.
/// Elapsed time is wall time. Profiling never consumes world RNG and is not saved with the world.
/// </summary>
public sealed class SimulationProfiler
{
    private readonly SampleBuffer[] buffers;
    private readonly int[] initialCollections = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];

    public SimulationProfiler(int retainedSamplesPerPhase = 2048)
    {
        if (retainedSamplesPerPhase < 1 || retainedSamplesPerPhase > 100_000)
            throw new ArgumentOutOfRangeException(nameof(retainedSamplesPerPhase));
        buffers = Enum.GetValues<SimulationPhase>().Select(_ => new SampleBuffer(retainedSamplesPerPhase)).ToArray();
    }

    public Scope Measure(SimulationPhase phase, int processedEntities = 0)
    {
        if (!Enum.IsDefined(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
        if (processedEntities < 0) throw new ArgumentOutOfRangeException(nameof(processedEntities));
        return new Scope(this, phase, processedEntities);
    }

    /// <summary>Call outside measured regions, on the simulation thread; allocates sorted copies.</summary>
    public SimulationProfileSnapshot Snapshot()
    {
        var phases = new List<PhaseProfile>();
        foreach (var phase in Enum.GetValues<SimulationPhase>())
        {
            var b = buffers[(int)phase];
            if (b.Count == 0) continue;
            var times = b.Times.Take(b.Count).Order().ToArray();
            double Percentile(double p) => times[(int)Math.Floor((times.Length - 1) * p)];
            phases.Add(new(phase, b.Count, b.TotalSamples, times.Average(), Percentile(.5),
                Percentile(.95), Percentile(.99), times[^1],
                b.Allocations.Take(b.Count).Average(v => (double)v), b.ProcessedEntities));
        }
        return new(phases.AsReadOnly(), GC.CollectionCount(0) - initialCollections[0],
            GC.CollectionCount(1) - initialCollections[1], GC.CollectionCount(2) - initialCollections[2]);
    }

    // A scope is owned by one using statement; do not copy or dispose it twice.
    public readonly struct Scope : IDisposable
    {
        private readonly SimulationProfiler owner;
        private readonly SimulationPhase phase;
        private readonly int entities;
        private readonly long start, allocationStart;
        internal Scope(SimulationProfiler owner, SimulationPhase phase, int entities)
        {
            this.owner = owner; this.phase = phase; this.entities = entities;
            allocationStart = GC.GetAllocatedBytesForCurrentThread();
            start = Stopwatch.GetTimestamp();
        }
        public void Dispose()
        {
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            var b = owner.buffers[(int)phase];
            b.Times[b.Cursor] = elapsed;
            b.Allocations[b.Cursor] = allocated;
            b.Cursor = (b.Cursor + 1) % b.Times.Length;
            b.Count = Math.Min(b.Count + 1, b.Times.Length);
            b.TotalSamples++;
            b.ProcessedEntities += entities;
        }
    }

    private sealed class SampleBuffer(int capacity)
    {
        internal readonly double[] Times = new double[capacity];
        internal readonly long[] Allocations = new long[capacity];
        internal int Cursor, Count;
        internal long TotalSamples, ProcessedEntities;
    }
}

public sealed partial class Simulation
{
    /// <summary>Null by default. Set only between ticks. Diagnostics are excluded from saves/replay state.</summary>
    public SimulationProfiler? Profiler { get; set; }
}

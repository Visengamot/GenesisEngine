using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Genesis.Engine.Core;
using Raylib_cs;

namespace Genesis.Engine.App;

/// <summary>Opt-in full-window benchmark of a seeded world evolved exclusively by Simulation.Step.</summary>
internal sealed class NaturalPopulationBenchmark
{
    private const int PrewarmTicks = 60 * 30, WarmupFrames = 30, MeasuredFrames = 120;
    private const float SimulationDt = 1f / 30;
    private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
    private readonly List<double> steps = [], draws = [], presents = [], frames = [];
    private readonly List<long> allocations = [];
    private long frameStart, stepStart, drawStart, presentStart, allocatedStart, measuredTickStart;
    private double stepMs;
    private int frame, prewarmInitialPopulation, prewarmEndPopulation, prewarmMinPopulation, prewarmMaxPopulation;
    private int populationStart, populationMin, populationMax, generationStart;
    private long birthsAtStart, birthsAtPrewarm, birthsAtMeasurementStart;
    private int[] collectionsStart = [];
    public bool IsComplete => frames.Count == MeasuredFrames;

    public Simulation CreateSimulation()
    {
        var simulation = new Simulation(seed: 42);
        prewarmInitialPopulation = prewarmMinPopulation = prewarmMaxPopulation = simulation.Agents.Count;
        birthsAtStart = NextId(simulation);
        Console.WriteLine($"Natural benchmark: prewarming seed {simulation.Seed} for {PrewarmTicks} headless ticks ({PrewarmTicks / 30}s simulation), no injected agents.");
        for (int i = 0; i < PrewarmTicks; i++)
        {
            simulation.Step(SimulationDt);
            prewarmMinPopulation = Math.Min(prewarmMinPopulation, simulation.Agents.Count);
            prewarmMaxPopulation = Math.Max(prewarmMaxPopulation, simulation.Agents.Count);
        }
        prewarmEndPopulation = simulation.Agents.Count;
        birthsAtPrewarm = NextId(simulation);
        Console.WriteLine($"Prewarm complete: population {simulation.Agents.Count}, range {prewarmMinPopulation}..{prewarmMaxPopulation}, births {birthsAtPrewarm - birthsAtStart}, generation {Generation(simulation)}.");
        return simulation;
    }

    public void BeginFrame(Simulation simulation)
    {
        if (frame == WarmupFrames)
        {
            populationStart = populationMin = populationMax = simulation.Agents.Count;
            generationStart = Generation(simulation);
            birthsAtMeasurementStart = NextId(simulation);
            measuredTickStart = simulation.Tick;
            collectionsStart = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        }
        allocatedStart = GC.GetAllocatedBytesForCurrentThread();
        frameStart = Stopwatch.GetTimestamp();
    }
    public void BeginStep() => stepStart = Stopwatch.GetTimestamp();
    public void EndStep() => stepMs = Milliseconds(stepStart, Stopwatch.GetTimestamp());
    public void BeginDraw() => drawStart = Stopwatch.GetTimestamp();
    public void BeginPresent() => presentStart = Stopwatch.GetTimestamp();

    public bool EndFrame(Simulation simulation)
    {
        long end = Stopwatch.GetTimestamp();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedStart;
        if (frame++ < WarmupFrames) return false;
        steps.Add(stepMs);
        draws.Add(Milliseconds(drawStart, end));
        presents.Add(Milliseconds(presentStart, end));
        frames.Add(Milliseconds(frameStart, end));
        allocations.Add(allocated);
        populationMin = Math.Min(populationMin, simulation.Agents.Count);
        populationMax = Math.Max(populationMax, simulation.Agents.Count);
        return IsComplete;
    }

    public void Save(Simulation simulation)
    {
        long finalBirthId = NextId(simulation);
        var report = new
        {
            mode = "--benchmark-natural", completed = IsComplete, startedUtc = started, finishedUtc = DateTimeOffset.UtcNow,
            runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(), logicalProcessors = Environment.ProcessorCount,
            stopwatchFrequency = Stopwatch.Frequency, targetFps = 0, requestedVsync = false,
            methodology = "Native Raylib window, same complete world renderer and HUD as gameplay; full frames include EndDrawing. Seed 42 initial 80 inhabitants; 1800 headless Simulation.Step(1/30) ticks, 30 native warmup frames, then 120 measured native frames, one complete simulation step per frame. No injections, external spawning, disabled systems, screenshot, or FPS limiter. Managed allocations are measured on the current thread. Percentile uses nearest rank. Wall-clock times may include driver/compositor presentation waits and are not isolated GPU time.",
            normalBirthCap = Simulation.MaximumPopulation, simulationDt = SimulationDt,
            prewarm = new { ticks = PrewarmTicks, initialPopulation = prewarmInitialPopulation, endPopulation = prewarmEndPopulation, minPopulation = prewarmMinPopulation, maxPopulation = prewarmMaxPopulation, births = birthsAtPrewarm - birthsAtStart },
            warmupFrames = WarmupFrames, measuredFramesRequested = MeasuredFrames, measuredFrames = frames.Count,
            measuredPopulation = new { start = populationStart, end = simulation.Agents.Count, min = populationMin, max = populationMax },
            generations = new { start = generationStart, end = Generation(simulation) },
            births = new { prewarm = birthsAtPrewarm - birthsAtStart, duringNativeWarmup = birthsAtMeasurementStart - birthsAtPrewarm, duringMeasurement = finalBirthId - birthsAtMeasurementStart, total = finalBirthId - birthsAtStart },
            simulationTicks = new { start = measuredTickStart, end = simulation.Tick, measured = simulation.Tick - measuredTickStart },
            stepMs = Summarize(steps), drawIncludingEndDrawingMs = Summarize(draws), endDrawingMs = Summarize(presents), frameMs = Summarize(frames),
            meanThroughputFps = frames.Count == 0 ? (double?)null : 1000 / frames.Average(),
            managedAllocationBytes = new { scope = "Current render/simulation thread, each measured frame; excludes sample storage and prewarm", total = allocations.Sum(), perFrame = Summarize(allocations.Select(x => (double)x)) },
            gcCollections = collectionsStart.Length == 3 ? Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - collectionsStart[i]).ToArray() : [],
            screen = new { width = Raylib.GetScreenWidth(), height = Raylib.GetScreenHeight(), renderWidth = Raylib.GetRenderWidth(), renderHeight = Raylib.GetRenderHeight() },
            world = new { width = simulation.Terrain.Width, height = simulation.Terrain.Height, seed = simulation.Seed, tribes = simulation.Tribes.Count, species = simulation.SpeciesCount }
        };
        string path = Path.Combine(Environment.CurrentDirectory, "docs", "natural-1000-render.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Natural benchmark {(IsComplete ? "complete" : "interrupted")}: {path}");
    }

    private static long NextId(Simulation simulation)
    {
        using var document = JsonDocument.Parse(simulation.SaveJson());
        return document.RootElement.GetProperty("NextId").GetInt64();
    }
    private static int Generation(Simulation simulation) => simulation.Agents.Count == 0 ? 0 : simulation.Agents.Max(a => a.Generation);
    private static double Milliseconds(long start, long end) => (end - start) * 1000.0 / Stopwatch.Frequency;
    private static object? Summarize(IEnumerable<double> samples)
    {
        var sorted = samples.Order().ToArray();
        return sorted.Length == 0 ? null : new { mean = sorted.Average(), min = sorted[0], p95 = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], max = sorted[^1] };
    }
}

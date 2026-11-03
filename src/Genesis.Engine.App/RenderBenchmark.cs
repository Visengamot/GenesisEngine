using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Genesis.Engine.Core;
using Raylib_cs;

namespace Genesis.Engine.App;

/// <summary>Opt-in native-window benchmark; all measured frames run the normal complete simulation and draw path.</summary>
internal sealed class RenderBenchmark
{
    private static readonly int[] Populations = [600, 2000, 10000];
    private const int WarmupFrames = 30, MeasuredFrames = 120;
    private readonly List<object> results = [];
    private readonly List<double> steps = [], draws = [], presents = [], frames = [];
    private readonly List<long> allocations = [];
    private int scenario, frame, populationStart, populationMin, populationMax;
    private long frameStart, stepStart, drawStart, presentStart, allocatedStart, tickStart;
    private double stepMs;
    private int[] collectionsStart = [];
    private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
    public bool IsComplete => scenario == Populations.Length;

    public Simulation CreateSimulation()
    {
        var simulation = new Simulation(seed: 42);
        // Deliberately bypass only the birth/injection cap, not any simulation subsystem.
        // Preserve the standard world, distribute independently seeded living agents throughout it.
        simulation.Agents.Clear();
        var random = new Random(1729);
        for (int i = 0; i < Populations[scenario]; i++)
            simulation.Agents.Add(new Agent(i,
                new Vec2(random.NextSingle() * (simulation.Terrain.Width - 1), random.NextSingle() * (simulation.Terrain.Height - 1)),
                Genome.Random(random), NeuralNetwork.CreateForager(random.Next()))
            { Heading = new Vec2(random.NextSingle() * 2 - 1, random.NextSingle() * 2 - 1) });
        frame = 0;
        steps.Clear(); draws.Clear(); presents.Clear(); frames.Clear(); allocations.Clear();
        Console.WriteLine($"Render benchmark: injected {Populations[scenario]}, warmup {WarmupFrames}, measured {MeasuredFrames}.");
        return simulation;
    }

    public void BeginFrame(Simulation simulation)
    {
        if (frame == WarmupFrames)
        {
            populationStart = populationMin = populationMax = simulation.Agents.Count;
            tickStart = simulation.Tick;
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
        if (frames.Count != MeasuredFrames) return false;
        var result = new
        {
            injectedPopulation = Populations[scenario],
            measuredPopulation = new { start = populationStart, end = simulation.Agents.Count, min = populationMin, max = populationMax },
            warmupFrames = WarmupFrames, measuredFrames = frames.Count,
            simulationTicks = new { start = tickStart, end = simulation.Tick, measured = simulation.Tick - tickStart },
            stepMs = Summarize(steps), drawIncludingEndDrawingMs = Summarize(draws),
            endDrawingMs = Summarize(presents), frameMs = Summarize(frames),
            meanThroughputFps = 1000 / frames.Average(),
            managedAllocationBytes = new { scope = "Current render/simulation thread, each measured frame; excludes sample storage and scenario setup", total = allocations.Sum(), perFrame = Summarize(allocations.Select(x => (double)x)) },
            gcCollections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - collectionsStart[i]).ToArray(),
            screen = new { width = Raylib.GetScreenWidth(), height = Raylib.GetScreenHeight(), renderWidth = Raylib.GetRenderWidth(), renderHeight = Raylib.GetRenderHeight() },
            world = new { width = simulation.Terrain.Width, height = simulation.Terrain.Height, seed = simulation.Seed, tribes = simulation.Tribes.Count, species = simulation.SpeciesCount }
        };
        results.Add(result);
        Console.WriteLine(JsonSerializer.Serialize(result));
        scenario++;
        return true;
    }

    public void Save()
    {
        string path = Path.Combine(Environment.CurrentDirectory, "render-benchmark-results.json");
        var report = new
        {
            mode = "--benchmark-render", completed = IsComplete, startedUtc = started, finishedUtc = DateTimeOffset.UtcNow,
            runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(), logicalProcessors = Environment.ProcessorCount,
            stopwatchFrequency = Stopwatch.Frequency, targetFps = 0, requestedVsync = false,
            methodology = "Native Raylib window; normal full world renderer and HUD, BeginDrawing through actual EndDrawing included. One complete Simulation.Step(1/30) per warmup and measured frame, never an accumulator skip. Independent seeded worlds; agents injected above normal birth cap, no simulation systems disabled. Population evolves naturally. Nearest-rank p95. CPU wall times include driver/presentation waits, not isolated GPU timings; uncapped request does not guarantee compositor/driver behavior or 60 FPS. UI mutation disabled only in benchmark mode. No screenshots.",
            normalBirthCap = Simulation.MaximumPopulation, injectionSeed = 1729, simulationDt = 1f / 30,
            warmupFramesPerScenario = WarmupFrames, measuredFramesPerScenario = MeasuredFrames,
            scenarios = results
        };
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Render benchmark {(IsComplete ? "complete" : "interrupted")}: {path}");
    }

    private static double Milliseconds(long start, long end) => (end - start) * 1000.0 / Stopwatch.Frequency;
    private static object Summarize(IEnumerable<double> samples)
    {
        var sorted = samples.Order().ToArray();
        return new { mean = sorted.Average(), p95 = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], max = sorted[^1] };
    }
}

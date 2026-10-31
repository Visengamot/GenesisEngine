using System.Diagnostics;
using System.Text.Json;
using Genesis.Engine.Core;

// Explicitly synthetic stress scenario: no renderer; injected population can exceed gameplay birth cap.
var populations = args.Length == 0 ? new[] { 600, 2000, 10000 } : args.Select(int.Parse).ToArray();
var results = new List<object>();
foreach (int count in populations)
{
    if (count < 1 || count > 20000) throw new ArgumentOutOfRangeException(nameof(args));
    var simulation = new Simulation(160, 90, 42);
    simulation.Agents.Clear();
    var random = new Random(93);
    for (int i=0;i<count;i++)
        simulation.Agents.Add(new Agent(i, new(random.NextSingle()*159,random.NextSingle()*89),
            Genome.Random(random), NeuralNetwork.CreateForager(random.Next())));
    simulation.RefreshSpecies();
    // Warm JIT and the environment. All systems, including social grouping, remain enabled.
    for(int i=0;i<30;i++) simulation.Step(1f/30);
    const int samples=120;
    var times=new double[samples];
    long allocated=GC.GetAllocatedBytesForCurrentThread();
    for(int i=0;i<samples;i++)
    {
        long start=Stopwatch.GetTimestamp();
        simulation.Step(1f/30);
        times[i]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
    Array.Sort(times);
    var result = new { initialPopulation=count, finalPopulation=simulation.Agents.Count,
        meanStepMs=times.Average(), p95StepMs=times[(int)(samples*.95)], maxStepMs=times[^1],
        allocatedBytesPerStep=allocated/samples, samples, seed=42, map="160x90", fixedStepSeconds=1f/30,
        runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        cpuCount=System.Environment.ProcessorCount, note="Headless synthetic stress; not rendered FPS, no ECS claim. Birth cap remains 600." };
    results.Add(result);
    Console.WriteLine(JsonSerializer.Serialize(result));
}
File.WriteAllText("benchmark-results.json",JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));

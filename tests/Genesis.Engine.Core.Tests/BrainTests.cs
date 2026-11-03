using Genesis.Engine.Core;
using System.Text.Json;

namespace Genesis.Engine.Core.Tests;

public class BrainTests
{
    private static readonly Genome Traits = new(1, 1, 3, 1, .5f, .5f, .5f);

    [Fact]
    public void BoundariesHaveNoFoodAndPresentAnObstacleRatherThanClampedSamples()
    {
        var sim = new Simulation(20, 20);
        var agent = new Agent(1000, new(0, 10), Traits);
        sim.Food.Set(0, 10, 1);
        var sensors = sim.Sense(agent);
        Assert.Equal(Agent.SensorCount, sensors.Length);
        Assert.Equal(0, sensors[1]);
        Assert.Equal(1, sensors[5]);
        Assert.Equal(1, sensors[9]);
    }

    [Fact]
    public void OutwardHeadingReflectsLocallyAndPersistsAwayFromWall()
    {
        foreach (var start in new[] { new Vec2(0, 10), new Vec2(19, 10), new Vec2(10, 0), new Vec2(10, 19) })
        {
            var sim = new Simulation(20, 20); sim.Agents.Clear();
            var outward = new Vec2(start.X == 0 ? -1 : start.X == 19 ? 1 : 0, start.Y == 0 ? -1 : start.Y == 19 ? 1 : 0);
            var agent = new Agent(1000, start, Traits, new NeuralNetwork(new float[2, Agent.SensorCount])) { Heading = outward };
            sim.Agents.Add(agent);
            sim.Step(.1f);
            Assert.InRange(Math.Abs(agent.Position.X - start.X) + Math.Abs(agent.Position.Y - start.Y), .01f, .5f);
            for (int i = 0; i < 10; i++) sim.Step(.1f);
            Assert.True(agent.Position.X > 0 && agent.Position.X < 19 && agent.Position.Y > 0 && agent.Position.Y < 19);
            Assert.True(agent.Heading.X * outward.X + agent.Heading.Y * outward.Y < 0);
        }
    }

    [Fact]
    public void SeededPopulationStaysDistributedWithoutEdgeOrCenterCollapse()
    {
        var sim = new Simulation(80, 60, 193);
        for (int step = 0; step < 600; step++) sim.Step(.05f);
        Assert.True(sim.Agents.Count > 20);
        float edgeFraction = sim.Agents.Count(a => a.Position.X < 1 || a.Position.Y < 1 || a.Position.X > 78 || a.Position.Y > 58) / (float)sim.Agents.Count;
        Assert.True(edgeFraction < .15f, $"Edge fraction: {edgeFraction}");
        Assert.InRange(sim.Agents.Average(a => a.Position.X), 20, 60);
        Assert.InRange(sim.Agents.Average(a => a.Position.Y), 15, 45);
        Assert.True(sim.Agents.Max(a => a.Position.X) - sim.Agents.Min(a => a.Position.X) > 50);
        Assert.True(sim.Agents.Max(a => a.Position.Y) - sim.Agents.Min(a => a.Position.Y) > 35);
    }

    [Fact]
    public void MultipleSeedsRetainLivingInteriorPopulationsAfterSixtySeconds()
    {
        foreach (int seed in new[] { 1, 42, 193, 777 })
        {
            var sim = new Simulation(80, 60, seed);
            for (int step = 0; step < 1200; step++) sim.Step(.05f);
            int edge = sim.Agents.Count(a => a.Position.X < 1 || a.Position.Y < 1 || a.Position.X > 78 || a.Position.Y > 58);
            int quadrants = sim.Agents.Select(a => (a.Position.X >= 40, a.Position.Y >= 30)).Distinct().Count();
            Console.WriteLine($"Distribution seed={seed}: population={sim.Agents.Count}, edge={edge}, quadrants={quadrants}, mean=({sim.Agents.Average(a => a.Position.X):F1},{sim.Agents.Average(a => a.Position.Y):F1})");
            Assert.True(sim.Agents.Count >= 30, $"Seed {seed}: population {sim.Agents.Count}");
            Assert.True(edge < sim.Agents.Count * .15f, $"Seed {seed}: edge {edge}/{sim.Agents.Count}");
            Assert.Equal(4, quadrants);
        }
    }

    [Fact]
    public void InitialForagerRespondsToOpposingFoodGradientsWithoutSharedBias()
    {
        var east = new float[Agent.SensorCount]; east[0] = .5f; east[1] = -.5f; east[11] = 1;
        var west = new float[Agent.SensorCount]; west[0] = -.5f; west[1] = .5f; west[11] = 1;
        for (int seed = 0; seed < 20; seed++)
        {
            var brain = NeuralNetwork.CreateForager(seed);
            Assert.True(brain.Evaluate(east)[0] > .5f);
            Assert.True(brain.Evaluate(west)[0] < -.5f);
            var neutral = new float[Agent.SensorCount]; neutral[11] = 1;
            Assert.Equal(0, brain.Evaluate(neutral)[0]);
            Assert.Equal(0, brain.Evaluate(neutral)[1]);
        }
        Assert.NotEqual(NeuralNetwork.CreateForager(1).GetWeights()[0, 0], NeuralNetwork.CreateForager(2).GetWeights()[0, 0]);
    }

    [Fact]
    public void StructuralInnovationsAreHomologousAndInheritedWithSnapshotRoundtrip()
    {
        var original = new NeuralNetwork(3, 2, 42);
        var first = original.MutateTopology(new Random(13), 1, 0);
        var same = original.MutateTopology(new Random(13), 1, 0);
        Assert.Equal(1, first.HiddenNodeCount);
        Assert.Equal(first.ToSnapshot().Nodes, same.ToSnapshot().Nodes);
        Assert.Equal(first.ToSnapshot().Connections, same.ToSnapshot().Connections);
        var child = first.Crossover(original, new Random(44), 0);
        Assert.Equal(1, child.HiddenNodeCount);
        Assert.Contains(child.ToSnapshot().Connections, c => c.From == first.ToSnapshot().Nodes.Single(n => n.Kind == NeuralNodeKind.Hidden).Id);
        var json = JsonSerializer.Serialize(child.ToSnapshot());
        var loaded = new NeuralNetwork(JsonSerializer.Deserialize<NetworkSnapshot>(json)!);
        Assert.Equal(child.Evaluate(new float[] { .2f, -.3f, .7f }), loaded.Evaluate(new float[] { .2f, -.3f, .7f }));
        var snapshot = loaded.ToSnapshot(); snapshot.Connections[0] = snapshot.Connections[0] with { Weight = 4 };
        Assert.Equal(child.Evaluate(new float[] { .2f, -.3f, .7f }), loaded.Evaluate(new float[] { .2f, -.3f, .7f }));
    }

    [Fact]
    public void SnapshotRejectsCyclesAndInvalidWeights()
    {
        var snapshot = new NeuralNetwork(2, 1).ToSnapshot();
        snapshot.Connections[0] = snapshot.Connections[0] with { Weight = float.NaN };
        Assert.Throws<ArgumentOutOfRangeException>(() => new NeuralNetwork(snapshot));
        var loop = new NetworkSnapshot(1, 1, [new(0, NeuralNodeKind.Input, 0, 0), new(1, NeuralNodeKind.Output, 0, 1)], [new(1L << 32, 1, 0, 1)]);
        Assert.Throws<ArgumentException>(() => new NeuralNetwork(loop));
    }
}

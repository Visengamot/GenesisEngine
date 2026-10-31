using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class LiveEcsTests
{
    [Fact]
    public void ExternalEditsAndReorderedAgentsAreReconciledBeforeMovement()
    {
        var sim = new Simulation(24, 24, 99);
        var first = sim.Agents[0];
        var second = sim.Agents[1];
        sim.Agents.Reverse();
        first.Position = new(12, 12);
        first.Energy = 2;
        first.Genome = new Genome(3, 1, 2, 1, 0, 0, 0);
        Assert.Equal(sim.Agents.Count, sim.EntityCount);
        Assert.True(sim.TryGetEntitySnapshot(first.Id, out var before));
        Assert.Equal(first.Position, before.Position.Value);
        Assert.Equal(2, before.Vitality.Energy);
        Assert.Equal(first.Genome, before.Genome.Value);
        Assert.Same(first.Brain, before.Brain.Network);
        sim.Step(.1f);
        Assert.True(sim.TryGetEntitySnapshot(first.Id, out var after));
        Assert.Equal(first.Position, after.Position.Value);
        Assert.Equal(first.Energy, after.Vitality.Energy);
        Assert.Equal(first.Genome, after.Genome.Value);
        Assert.True(sim.TryGetEntitySnapshot(second.Id, out _));
    }

    [Fact]
    public void PublicBirthDeathReplacementAndEraseReconcileById()
    {
        var sim = new Simulation(20, 20, 42);
        var removed = sim.Agents[0];
        sim.Agents.Remove(removed);
        Assert.False(sim.TryGetEntitySnapshot(removed.Id, out _));
        var replacement = new Agent(removed.Id, new(4, 5), removed.Genome, removed.Brain) { Energy = 2 };
        sim.Agents.Add(replacement);
        Assert.True(sim.TryGetEntitySnapshot(replacement.Id, out var snapshot));
        Assert.Equal(replacement.Position, snapshot.Position.Value);
        var spawned = sim.SpawnAgent(new(10, 10));
        Assert.Equal(sim.Agents.Count, sim.EntityCount);
        Assert.True(sim.TryGetEntitySnapshot(spawned.Id, out _));
        replacement.Energy = 0;
        sim.Step(.05f);
        Assert.False(sim.TryGetEntitySnapshot(replacement.Id, out _));
        Assert.Equal(sim.Agents.Count, sim.EntityCount);
        sim.ApplyTool(GodTool.Erase, spawned.Position, 1, 1);
        Assert.False(sim.TryGetEntitySnapshot(spawned.Id, out _));
    }

    [Fact]
    public void SnapshotRoundTripAndSubsequentMovementMatchWithLiveComponents()
    {
        var original = new Simulation(18, 18, 7);
        original.Agents[0].Position = new(9, 9);
        original.Agents[0].Energy = 2;
        original.Step(.1f);
        var restored = Simulation.LoadJson(original.SaveJson());
        Assert.Equal(restored.Agents.Count, restored.EntityCount);
        for (int i = 0; i < 12; i++)
        {
            original.Step(.05f);
            restored.Step(.05f);
        }
        Assert.Equal(original.SaveJson(), restored.SaveJson());
        foreach (var agent in restored.Agents)
        {
            Assert.True(restored.TryGetEntitySnapshot(agent.Id, out var ecs));
            Assert.Equal(agent.Position, ecs.Position.Value);
            Assert.Equal(agent.Energy, ecs.Vitality.Energy);
            Assert.Equal(agent.Genome, ecs.Genome.Value);
            Assert.Same(agent.Brain, ecs.Brain.Network);
        }
    }

    [Fact]
    public void ZeroStepKeepsExistingNoOpAndDiagnosticsReadsReconcileWithoutMovement()
    {
        var sim = new Simulation(10, 10);
        var agent = sim.Agents[0];
        agent.Position = new(3, 4);
        sim.Step(0);
        Assert.Equal(0, sim.Tick);
        Assert.True(sim.TryGetEntitySnapshot(agent.Id, out var ecs));
        Assert.Equal(new Vec2(3, 4), ecs.Position.Value);
    }
}

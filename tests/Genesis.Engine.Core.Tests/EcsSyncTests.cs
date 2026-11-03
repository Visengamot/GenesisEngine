using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class EcsSyncTests
{
    [Fact]
    public void DirectListChangesReconcileWithoutLeavingStaleHandles()
    {
        var sim = new Simulation(16, 16, 37);
        var removed = sim.Agents[0];
        var retained = sim.Agents[1];
        sim.Agents.RemoveAt(0);
        sim.Agents.Reverse();
        retained.Position = new(5, 6);
        retained.Energy = 2;
        Assert.Equal(sim.Agents.Count, sim.EntityCount);
        Assert.False(sim.TryGetEntitySnapshot(removed.Id, out _));
        Assert.True(sim.TryGetEntitySnapshot(retained.Id, out var updated));
        Assert.Equal(retained.Position, updated.Position.Value);
        Assert.Equal(retained.Energy, updated.Vitality.Energy);

        var replacement = new Agent(removed.Id, new(7, 8), removed.Genome, removed.Brain) { Energy = 1.5f };
        sim.Agents.Insert(0, replacement);
        Assert.Equal(sim.Agents.Count, sim.EntityCount);
        Assert.True(sim.TryGetEntitySnapshot(removed.Id, out var recreated));
        Assert.Equal(replacement.Position, recreated.Position.Value);
        Assert.Equal(replacement.Energy, recreated.Vitality.Energy);
        Assert.Equal(sim.SaveJson(), Simulation.LoadJson(sim.SaveJson()).SaveJson());

        sim.Agents.Clear();
        Assert.Equal(0, sim.EntityCount);
        Assert.False(sim.TryGetEntitySnapshot(removed.Id, out _));
        Assert.False(sim.TryGetEntitySnapshot(retained.Id, out _));
    }

    [Fact]
    public void RepeatedValidationAndReconciliationDoNotAllocatePerAgentOrSnapshot()
    {
        var sim = new Simulation(16, 16, 48);
        var model = sim.Agents[0];
        for (int id = 80; id < Simulation.MaximumPopulation; id++)
            sim.Agents.Add(new Agent(id, new(3, 3), model.Genome, model.Brain));
        Assert.Equal(Simulation.MaximumPopulation, sim.EntityCount);

        for (int i = 0; i < 32; i++) _ = sim.EntityCount;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) _ = sim.EntityCount;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // Previous implementation allocated two HashSets plus Dictionary.ToArray per access
        // (tens of kilobytes at this population). A stable registry needs no per-call buffers.
        Assert.True(allocated < 16_000, $"100 reconciliations allocated {allocated:N0} bytes.");
    }
}

using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class EvolutionAllocationTests
{
    private static readonly Genome Traits = new(1, 1, 2, 1, .5f, .5f, .5f);

    private static Simulation AtCapacity()
    {
        var simulation = new Simulation(1, 1, 410);
        simulation.Agents.Clear();
        var brain = new NeuralNetwork(new float[2, Agent.SensorCount]);
        for (int i = 0; i < Simulation.MaximumPopulation; i++)
            simulation.Agents.Add(new Agent(1000 + i, default, Traits, brain) { Energy = 2 });
        for (int step = 0; step < 6; step++)
        {
            foreach (var agent in simulation.Agents) agent.Energy = 2;
            simulation.Step(1);
        }
        simulation.RefreshSpecies();
        return simulation;
    }

    [Fact]
    public void UnchangedMembershipRefreshDoesNotAllocate()
    {
        var simulation = AtCapacity();
        simulation.RefreshSpecies(); // Warm up the unchanged-membership path.
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) simulation.RefreshSpecies();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(Simulation.MaximumPopulation, Assert.Single(simulation.Species).MemberCount);
    }

    [Fact]
    public void FullPopulationSkipsBirthButRefreshesExternallyChangedMembership()
    {
        var simulation = AtCapacity();
        var incompatibleWeights = new float[2, Agent.SensorCount];
        for (int row = 0; row < 2; row++)
            for (int column = 0; column < Agent.SensorCount; column++) incompatibleWeights[row, column] = 4;
        simulation.Agents[0] = new Agent(2000, default, Traits,
            new NeuralNetwork(incompatibleWeights)) { Energy = 2 };
        simulation.Step(.001f);
        Assert.Equal(Simulation.MaximumPopulation, simulation.Agents.Count);
        Assert.All(simulation.Agents, agent => Assert.Equal(0, agent.OffspringCount));
        Assert.Equal(2, simulation.SpeciesCount);
        Assert.Equal(Simulation.MaximumPopulation, simulation.Species.Sum(s => s.MemberCount));
        Assert.Equal(1, simulation.Species.Single(s => s.Id == simulation.Agents[0].SpeciesId).MemberCount);
        simulation.Agents[0] = new Agent(2001, default, Traits, simulation.Agents[1].Brain) { Energy = 2 };
        simulation.Step(.001f);
        Assert.Equal(Simulation.MaximumPopulation, Assert.Single(simulation.Species).MemberCount);
        Assert.All(simulation.Agents, agent => Assert.Equal(0, agent.OffspringCount));
    }

    [Fact]
    public void CappedSaveReplayRemainsIdenticalWhenBirthCapacityReturns()
    {
        var original = AtCapacity();
        original.Step(.001f);
        var restored = Simulation.LoadJson(original.SaveJson());
        Assert.Equal(original.SaveJson(), restored.SaveJson());
        original.Step(.001f);
        restored.Step(.001f);
        Assert.Equal(original.SaveJson(), restored.SaveJson());
        original.Agents.RemoveAt(0);
        restored.Agents.RemoveAt(0);
        original.Step(.001f);
        restored.Step(.001f);
        Assert.Equal(Simulation.MaximumPopulation, original.Agents.Count);
        Assert.Single(original.Agents.Where(agent => agent.Generation == 1));
        Assert.Equal(original.SaveJson(), restored.SaveJson());
    }
}

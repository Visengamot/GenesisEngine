using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class PopulationLimitTests
{
    [Fact]
    public void DefaultsAndAllowedRangeArePerSimulation()
    {
        var first = new Simulation(20, 20, 14);
        var second = new Simulation(20, 20, 14);
        Assert.Equal(80, first.Agents.Count);
        Assert.Equal(Simulation.MaximumPopulation, first.PopulationLimit);
        Assert.Equal(1000, second.PopulationLimit);
        first.SetPopulationLimit(Simulation.MinimumPopulationLimit);
        Assert.Equal(80, first.PopulationLimit);
        first.SetPopulationLimit(Simulation.AbsoluteMaximumPopulation);
        Assert.Equal(5000, first.PopulationLimit);
        Assert.Equal(1000, second.PopulationLimit);
        foreach (int invalid in new[] { int.MinValue, 0, 79, 5001, int.MaxValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => first.SetPopulationLimit(invalid));
            Assert.Equal(5000, first.PopulationLimit);
        }
    }

    [Fact]
    public void ManualSpawnsRespect150CapWithoutChangingDefaultCap()
    {
        var simulation = new Simulation(20, 20, 12);
        simulation.SetPopulationLimit(150);
        for (int i = 80; i < 150; i++) simulation.SpawnAgent(new Vec2(1, 1));
        Assert.Equal(150, simulation.Agents.Count);
        Assert.Throws<InvalidOperationException>(() => simulation.SpawnAgent(new Vec2(1, 1)));
        simulation.Step(.1f);
        Assert.Equal(150, simulation.Agents.Count);
        Assert.Equal(150, simulation.PopulationLimit);
    }

    [Fact]
    public void RaisingLimitAbove1000AllowsAdditionalSpawns()
    {
        var simulation = new Simulation(20, 20, 12);
        simulation.SetPopulationLimit(1500);
        // The public agent list is authoritative; use it to reach the old default cap efficiently.
        for (int id = 80; id < 1000; id++)
            simulation.Agents.Add(new Agent(id, new Vec2(1, 1), simulation.Agents[0].Genome,
                simulation.Agents[0].Brain));
        var born = simulation.SpawnAgent(new Vec2(1, 1));
        Assert.Equal(1000, born.Id);
        Assert.Equal(1001, simulation.Agents.Count);
    }

    [Fact]
    public void LoweringBelowLivingPopulationNeverKillsAndBlocksNewAgentsUntilBelowCap()
    {
        var simulation = new Simulation(20, 20, 12);
        for (int i = 0; i < 20; i++) simulation.SpawnAgent(new Vec2(1, 1));
        simulation.SetPopulationLimit(80);
        Assert.Equal(100, simulation.Agents.Count);
        Assert.Throws<InvalidOperationException>(() => simulation.SpawnAgent(new Vec2(1, 1)));
        simulation.Step(.1f);
        Assert.Equal(100, simulation.Agents.Count);
        Assert.True(simulation.Agents.RemoveAll(a => a.Id >= 80) == 20);
        Assert.Throws<InvalidOperationException>(() => simulation.SpawnAgent(new Vec2(1, 1)));
        simulation.Agents[0].Energy = 0;
        simulation.Step(.1f);
        Assert.Equal(79, simulation.Agents.Count);
        simulation.SpawnAgent(new Vec2(1, 1));
        Assert.Equal(80, simulation.Agents.Count);
    }

    [Fact]
    public void NonDefaultLimitProducesDeterministicReplay()
    {
        static (int Count, string Agents) Run()
        {
            var simulation = new Simulation(20, 20, 79);
            simulation.SetPopulationLimit(150);
            for (int i = 0; i < 12; i++) simulation.Step(.25f);
            return (simulation.Agents.Count, string.Join(';', simulation.Agents.Select(a =>
                $"{a.Id}:{a.Position.X:R}:{a.Position.Y:R}:{a.Energy:R}")));
        }
        Assert.Equal(Run(), Run());
    }
}

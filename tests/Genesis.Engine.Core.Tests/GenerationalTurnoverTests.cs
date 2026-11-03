using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class GenerationalTurnoverTests
{
    [Fact]
    public void SeededDefaultWorldHasBirthsAndDeathsWithoutAlwaysFillingPopulationCap()
    {
        var simulation = new Simulation(160, 90, 42);
        var previousIds = simulation.Agents.Select(a => a.Id).ToHashSet();
        long births = 0, deaths = 0;
        int cappedTicks = 0;

        // Reproduction and mortality should coexist; 1000 is a safety ceiling,
        // not a population target that the ordinary ecology must always hit.
        for (int tick = 1; tick <= 60 * 30; tick++)
        {
            simulation.Step(1f / 30);
            var currentIds = simulation.Agents.Select(a => a.Id).ToHashSet();
            births += currentIds.Count(id => !previousIds.Contains(id));
            deaths += previousIds.Count(id => !currentIds.Contains(id));
            previousIds = currentIds;
            Assert.InRange(simulation.Agents.Count, 0, Simulation.MaximumPopulation);
            if (simulation.Agents.Count == Simulation.MaximumPopulation) cappedTicks++;
        }

        Assert.True(cappedTicks < 60 * 30 / 10, $"Population stayed at the safety limit for {cappedTicks} ticks.");
        Assert.True(births > 80, $"Only {births} natural births occurred.");
        Assert.True(deaths > 0, "No natural deaths occurred during the 60-second scenario.");
        Assert.Equal(deaths, simulation.LifetimeDeaths);
        Assert.True(simulation.Agents.Any(a => a.Generation >= 2), "No multi-generation descendants survived.");
    }
}

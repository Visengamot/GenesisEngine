using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class GenerationalTurnoverTests
{
    [Fact]
    public void SeededDefaultWorldReplacesAgentsAfterFirstReachingPopulationCap()
    {
        var simulation = new Simulation(160, 90, 42);
        var previousIds = simulation.Agents.Select(a => a.Id).ToHashSet();
        long births = 0, deaths = 0, birthsAtFirstCap = -1;
        int firstCapTick = -1;

        // This seed first reaches the cap around tick 978; 60 simulated seconds
        // leave time for turnover without the cost of a 90+ second CI scenario.
        for (int tick = 1; tick <= 60 * 30; tick++)
        {
            simulation.Step(1f / 30);
            var currentIds = simulation.Agents.Select(a => a.Id).ToHashSet();
            births += currentIds.Count(id => !previousIds.Contains(id));
            deaths += previousIds.Count(id => !currentIds.Contains(id));
            previousIds = currentIds;
            Assert.InRange(simulation.Agents.Count, 0, Simulation.MaximumPopulation);
            if (firstCapTick < 0 && simulation.Agents.Count == Simulation.MaximumPopulation)
            {
                firstCapTick = tick;
                birthsAtFirstCap = births;
            }
        }

        Assert.True(firstCapTick > 0, "The default seed did not reach its cap within 60 seconds.");
        Assert.True(births > birthsAtFirstCap,
            $"No births followed the first cap at tick {firstCapTick}: {birthsAtFirstCap} then {births}.");
        Assert.True(deaths > 0, "No natural deaths occurred during the 60-second scenario.");
        Assert.True(simulation.Agents.Any(a => a.Generation >= 2), "No multi-generation descendants survived.");
    }
}

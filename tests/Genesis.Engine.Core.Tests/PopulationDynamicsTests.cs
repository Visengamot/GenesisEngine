using System.Text.Json;
using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class PopulationDynamicsTests
{
    [Fact]
    public void SeededDefaultWorldReproducesAndEvolvesDuringThirtySeconds()
    {
        static (long Births, int Population, int MaxGeneration, int Species) Run()
        {
            var simulation = new Simulation(160, 90, 42);
            const int initialPopulation = 80;
            for (int tick = 0; tick < 30 * 30; tick++) simulation.Step(1f / 30);

            // Surviving parents' OffspringCount omits births whose parents have died.
            // The persisted NextId is a lifetime counter, including deceased offspring.
            using var save = JsonDocument.Parse(simulation.SaveJson());
            long births = save.RootElement.GetProperty("NextId").GetInt64() - initialPopulation;
            Assert.True(births >= 100, $"Only {births} natural births in 30 seconds.");
            Assert.True(simulation.Agents.Count > initialPopulation,
                $"Population never grew beyond {initialPopulation} founders.");
            Assert.InRange(simulation.Agents.Count, 1, Simulation.MaximumPopulation);
            int generation = simulation.Agents.Max(agent => agent.Generation);
            Assert.True(generation >= 2, $"Evolution stopped at generation {generation}.");
            return (births, simulation.Agents.Count, generation, simulation.SpeciesCount);
        }

        Assert.Equal(Run(), Run()); // Replay the complete ecological scenario with the same seed.
    }
}

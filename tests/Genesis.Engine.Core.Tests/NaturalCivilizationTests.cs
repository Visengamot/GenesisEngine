using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class NaturalCivilizationTests
{
    [Fact]
    public void OrdinaryWorldDevelopsCivilizationWithoutManualResourceInjection()
    {
        var world = new Simulation(160, 90, 42);
        int discoveries = 0, advanced = 0, maximumTribes = 0;
        for (int tick = 0; tick < 120 * 30; tick++)
        {
            world.Step(1f / 30);
            discoveries = Math.Max(discoveries, world.Tribes.Sum(t => t.Research.Count(r => r.Unlocked)));
            advanced = Math.Max(advanced, world.Tribes.Count(t => t.Phase >= CivilizationPhase.Skilled));
            maximumTribes = Math.Max(maximumTribes, world.Tribes.Count);
        }
        Assert.True(maximumTribes > 0, "No natural tribes were founded.");
        Assert.True(discoveries > 0,
            $"No paid technology appeared in a natural world: tribes {world.Tribes.Count}, maximum {maximumTribes}.");
        Assert.True(advanced > 0,
            $"No tribe reached the Skilled stage by 120 simulated seconds; latest tribes {world.Tribes.Count}.");
    }
}

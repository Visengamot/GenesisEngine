using System.Text.Json;
using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class CapReplayTests
{
    [Fact]
    public void NaturallyEvolvingWorldContinuesBirthDeathAndReplayAfterSave()
    {
        var original = new Simulation(160, 90, 42);
        for (int tick = 0; tick < 40 * 30; tick++) original.Step(1f / 30);
        Assert.InRange(original.Agents.Count, 1, Simulation.MaximumPopulation - 1);
        string snapshot = original.SaveJson();
        var restored = Simulation.LoadJson(snapshot);
        Assert.Equal(snapshot, restored.SaveJson());
        using var initial = JsonDocument.Parse(snapshot);
        long startId = initial.RootElement.GetProperty("NextId").GetInt64();
        for (int tick = 0; tick < 30 * 30; tick++)
        {
            original.Step(1f / 30);
            restored.Step(1f / 30);
        }
        var final = original.SaveJson();
        using var parsed = JsonDocument.Parse(final);
        Assert.True(parsed.RootElement.GetProperty("NextId").GetInt64() > startId,
            "Births must continue in a naturally evolving population.");
        Assert.InRange(original.Agents.Count, 1, Simulation.MaximumPopulation);
        Assert.Equal(final, restored.SaveJson());
        Assert.True(original.Agents.Max(a => a.Generation) >= 5);
    }
}

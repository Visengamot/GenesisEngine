using System.Text.Json;
using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class CapReplayTests
{
    [Fact]
    public void NaturallyFilledWorldContinuesBirthDeathAndReplayAfterSave()
    {
        var original = new Simulation(160, 90, 42);
        for (int tick = 0; tick < 40 * 30; tick++) original.Step(1f / 30);
        Assert.Equal(Simulation.MaximumPopulation, original.Agents.Count);
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
            "Births must continue after reaching the live population cap.");
        Assert.Equal(Simulation.MaximumPopulation, original.Agents.Count);
        Assert.Equal(final, restored.SaveJson());
        Assert.True(original.Agents.Max(a => a.Generation) >= 5);
    }
}

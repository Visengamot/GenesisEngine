using System.Text.Json;
using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class ConfigurableWorldSaveTests
{
    [Fact]
    public void VersionSixPersistsRaisedAndLoweredLimitAcrossDeterministicReplay()
    {
        var original = new Simulation(20, 20, 13);
        original.SetPopulationLimit(1200);
        for (int i = 0; i < 100; i++) original.SpawnAgent(new Vec2(1, 1));
        original.SetPopulationLimit(80);
        Assert.Equal(180, original.Agents.Count);
        string json = original.SaveJson();
        var snapshot = JsonSerializer.Deserialize<WorldSave>(json)!;
        Assert.Equal(6, snapshot.Version);
        Assert.Equal(80, snapshot.PopulationLimit);
        var loaded = Simulation.LoadJson(json);
        Assert.Equal(80, loaded.PopulationLimit);
        Assert.Equal(json, loaded.SaveJson());
        for (int i = 0; i < 30; i++) { original.Step(1f / 30); loaded.Step(1f / 30); }
        Assert.Equal(original.SaveJson(), loaded.SaveJson());
    }

    [Fact]
    public void LegacyDefaultsToThousandAndInvalidNewLimitsAreRejected()
    {
        var snapshot = JsonSerializer.Deserialize<WorldSave>(new Simulation(20, 20).SaveJson())!;
        snapshot.PopulationLimit = 0;
        Assert.Throws<ArgumentException>(() => Simulation.LoadJson(JsonSerializer.Serialize(snapshot)));
        snapshot.PopulationLimit = 5001;
        Assert.Throws<ArgumentException>(() => Simulation.LoadJson(JsonSerializer.Serialize(snapshot)));
        snapshot.Version = 5;
        var restored = Simulation.LoadJson(JsonSerializer.Serialize(snapshot));
        Assert.Equal(1000, restored.PopulationLimit);
        Assert.Equal(6, JsonSerializer.Deserialize<WorldSave>(restored.SaveJson())!.Version);
    }

    [Fact]
    public void CivilizationResearchProgressAndBranchUnlocksSurviveRoundTrip()
    {
        var world = new Simulation(24, 24, 41);
        for (int i = 0; i < 30; i++) world.Step(1);
        var saved = world.SaveJson();
        var restored = Simulation.LoadJson(saved);
        Assert.Equal(saved, restored.SaveJson());
        Assert.All(restored.Tribes, tribe =>
        {
            Assert.InRange(tribe.FoodKnowledge, 0, 10_000);
            Assert.InRange(tribe.WoodKnowledge, 0, 10_000);
            Assert.InRange(tribe.StoneKnowledge, 0, 10_000);
        });
        for (int i = 0; i < 20; i++) { world.Step(1); restored.Step(1); }
        Assert.Equal(world.SaveJson(), restored.SaveJson());
    }
}

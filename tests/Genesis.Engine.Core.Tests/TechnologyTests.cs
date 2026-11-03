using System.Text.Json;
using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class TechnologyTests
{
    private static WorldSave Scenario(float age = 31)
    {
        var world = new Simulation(12, 12, 19);
        var data = JsonSerializer.Deserialize<WorldSave>(world.SaveJson())!;
        data.Agents = [new AgentSave { Id = 200, Position = new(5, 5), Energy = 1, Age = 1,
            Traits = [0.1f, 0.25f, 2, 0.1f, 0, 1, 0],
            Brain = new NeuralNetwork(new float[2, Agent.SensorCount]).ToSnapshot() }];
        Array.Fill(data.Terrain, .1f);
        Array.Fill(data.Food, 0);
        Array.Fill(data.Environment, new EnvironmentCell(.2f, 22, .7f, .7f, .7f, 0));
        data.Tribes = [new TribeSave { Id = 0, Center = new(5, 5), Members = [200], Age = age,
            Wood = 10, Stone = 10, Food = 10, WoodKnowledge = 10, StoneKnowledge = 10, FoodKnowledge = 10,
            HasWeapons = true, HasFarming = true }];
        data.Structures = [new Structure(StructureKind.Hut, new(5, 5), 0)];
        return data;
    }

    [Fact]
    public void PaidTechnologiesAdvanceAndPersistWithoutUpgradingLegacySaves()
    {
        var data = Scenario();
        var world = Simulation.LoadJson(JsonSerializer.Serialize(data));
        world.Step(1);
        var tribe = Assert.Single(world.Tribes);
        Assert.True(tribe.HasToolmaking);
        Assert.False(tribe.HasMedicine && tribe.HasFireproofing); // At most one discovery per decision.
        for (int i = 0; i < 5; i++) world.Step(1);
        Assert.True(tribe.HasMedicine);
        Assert.True(tribe.HasFireproofing);
        Assert.Contains(world.Events, e => e.Contains("изготовление орудий"));
        Assert.Contains(world.Events, e => e.Contains("лечение"));
        Assert.Contains(world.Events, e => e.Contains("просеки"));
        var loaded = Simulation.LoadJson(world.SaveJson());
        Assert.True(loaded.Tribes[0].HasToolmaking && loaded.Tribes[0].HasMedicine && loaded.Tribes[0].HasFireproofing);
        Assert.Equal(world.SaveJson(), loaded.SaveJson());
        var legacy = JsonSerializer.Deserialize<WorldSave>(world.SaveJson())!;
        legacy.Version = 2;
        var oldWorld = Simulation.LoadJson(JsonSerializer.Serialize(legacy));
        Assert.False(oldWorld.Tribes[0].HasToolmaking);
        Assert.False(oldWorld.Tribes[0].HasMedicine);
        Assert.False(oldWorld.Tribes[0].HasFireproofing);
    }

    [Fact]
    public void StagedPrerequisitesAndResourcesAreRequired()
    {
        var data = Scenario(age: 20);
        data.Tribes[0].HasWeapons = false;
        data.Tribes[0].HasFarming = false;
        data.Tribes[0].Wood = 0;
        data.Tribes[0].Stone = 0;
        data.Tribes[0].Food = 0;
        var world = Simulation.LoadJson(JsonSerializer.Serialize(data));
        world.Step(1);
        Assert.False(world.Tribes[0].HasToolmaking || world.Tribes[0].HasMedicine || world.Tribes[0].HasFireproofing);
    }
}

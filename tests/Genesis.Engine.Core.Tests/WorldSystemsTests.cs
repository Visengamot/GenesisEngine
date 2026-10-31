using System.Text.Json;
using Genesis.Engine.Core;
using Xunit;

namespace Genesis.Engine.Core.Tests;

public class WorldSystemsTests
{
    [Fact]
    public void BrushesValidateAndClampResources()
    {
        var sim = new Simulation(12, 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.ApplyTool(GodTool.Fire, new(2, 2), 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.ApplyTool(GodTool.Fire, new(float.NaN, 2), 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.ApplyTool((GodTool)99, new(2, 2), 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.ApplyTool(GodTool.Water, new(2, 2), 1, -1));
        sim.ApplyTool(GodTool.Heat, new(2, 2), 2, float.MaxValue);
        sim.ApplyTool(GodTool.Water, new(2, 2), 2, float.MaxValue);
        Assert.Equal(60, sim.Environment.Get(2, 2).Temperature);
        Assert.Equal(1, sim.Environment.Get(2, 2).Moisture);
        sim.ApplyTool(GodTool.Cool, new(2, 2), 2, 100);
        Assert.Equal(-50, sim.Environment.Get(2, 2).Temperature);
        sim.ApplyTool(GodTool.Raise, new(float.MaxValue, float.MaxValue), float.MaxValue, float.MaxValue);
    }

    [Fact]
    public void ClimateAndFireChangeFoodAndEnergy()
    {
        var warm = new Simulation(8, 8, 8); var burned = new Simulation(8, 8, 8);
        warm.Agents.Clear(); burned.Agents.Clear();
        var genome = new Genome(1, 1, 2, 1, .2f, .2f, .2f);
        warm.Agents.Add(new(100, new(3, 3), genome)); burned.Agents.Add(new(100, new(3, 3), genome));
        burned.ApplyTool(GodTool.Fire, new(3, 3), 100, 1);
        burned.ApplyTool(GodTool.Heat, new(3, 3), 100, 5);
        warm.Step(.1f); burned.Step(.1f);
        Assert.True(burned.Food.Get(3, 3) < warm.Food.Get(3, 3));
        Assert.True(burned.Agents[0].Energy < warm.Agents[0].Energy);
        for (int i = 0; i < 100; i++) burned.Step(.1f);
        var c = burned.Environment.Get(3, 3);
        Assert.InRange(c.Fire, 0, 1); Assert.InRange(c.Wood, 0, 1); Assert.InRange(c.Temperature, -50, 60);
    }

    [Fact]
    public void PredatorAttackAndEraseHaveObservableEffects()
    {
        var sim = new Simulation(10, 10); sim.Agents.Clear();
        sim.Agents.Add(new(100, new(3, 3), new Genome(1, 1, 2, 1, .2f, .2f, .2f)));
        sim.ApplyTool(GodTool.Predator, new(3, 3), 1, 1);
        sim.Step(.1f);
        Assert.Single(sim.Predators); Assert.True(sim.Predators[0].Energy > 1);
        sim.ApplyTool(GodTool.Erase, new(3, 3), 3, 1);
        Assert.Empty(sim.Predators); Assert.Empty(sim.Agents);
    }

    [Fact]
    public void SocialGroupBuildsAndPaysForTechnology()
    {
        var sim = new Simulation(10, 10); sim.Agents.Clear();
        var genome = new Genome(.1f, .25f, 2, .1f, .1f, 1, .1f);
        for (int i = 0; i < 4; i++) sim.Agents.Add(new(100 + i, new(3, 3), genome));
        for (int i = 0; i < 30; i++)
        {
            foreach (var a in sim.Agents) { a.Position = new(3, 3); a.Energy = 1; }
            sim.ApplyTool(GodTool.Forest, new(3, 3), 10, 1); sim.ApplyTool(GodTool.Stone, new(3, 3), 10, 1); sim.ApplyTool(GodTool.Food, new(3, 3), 10, 1);
            sim.Step(1);
        }
        Assert.Single(sim.Tribes);
        Assert.True(sim.Tribes[0].HasFarming, $"Age={sim.Tribes[0].Age} wood={sim.Tribes[0].Wood} food={sim.Tribes[0].Food} members={sim.Tribes[0].MemberIds.Count}"); Assert.True(sim.Tribes[0].HasWeapons);
        Assert.Contains(sim.Structures, s => s.Kind == StructureKind.Hut);
        Assert.Contains(sim.Structures, s => s.Kind == StructureKind.Farm);
        Assert.InRange(sim.Tribes[0].Wood, 0, 3);
        Assert.InRange(sim.Tribes[0].Stone, 0, 2);
    }

    [Fact]
    public void VersionedSaveRestoresEnvironmentAndBrain()
    {
        var original = new Simulation(8, 8, 123);
        original.ApplyTool(GodTool.Fire, new(2, 2), 2, .8f);
        original.ApplyTool(GodTool.Predator, new(6, 6), 1, 1);
        original.Step(.1f);
        string json = original.SaveJson(); var restored = Simulation.LoadJson(json);
        Assert.Equal(original.Tick, restored.Tick); Assert.Equal(original.Seed, restored.Seed);
        Assert.Equal(original.Environment.Get(2, 2), restored.Environment.Get(2, 2));
        Assert.Equal(original.Agents.Count, restored.Agents.Count); Assert.Single(restored.Predators);
        Assert.Equal(original.Agents[0].Position, restored.Agents[0].Position);
        Assert.Equal(original.Agents[0].Genome, restored.Agents[0].Genome);
        Assert.Equal(JsonSerializer.Serialize(original.Agents[0].Brain.ToSnapshot()), JsonSerializer.Serialize(restored.Agents[0].Brain.ToSnapshot()));
        restored.Step(.1f);
        Assert.Throws<NotSupportedException>(() => Simulation.LoadJson(json.Replace("\"Version\":1", "\"Version\":999")));
    }

    [Fact]
    public void FireSpreadsSynchronouslyAndWaterCreatesShallows()
    {
        var sim = new Simulation(8, 8); sim.Agents.Clear();
        sim.ApplyTool(GodTool.Forest, new(3, 3), 20, 1);
        sim.ApplyTool(GodTool.Dry, new(3, 3), 20, 1);
        sim.ApplyTool(GodTool.Fire, new(3, 3), .5f, 1);
        Assert.Equal(0, sim.Environment.Get(4, 3).Fire);
        sim.Step(.1f);
        Assert.True(sim.Environment.Get(4, 3).Fire > 0);
        Assert.Equal(0, sim.Environment.Get(5, 3).Fire);
        sim.ApplyTool(GodTool.Water, new(3, 3), 1, 1);
        Assert.True(sim.Terrain.Get(3, 3) <= -.25f);
        sim.ApplyTool(GodTool.Dry, new(3, 3), 1, 1);
        Assert.True(sim.Terrain.Get(3, 3) > 0);
    }

    [Fact]
    public void EraseThenSaveDoesNotRetainInvalidMembership()
    {
        var sim = new Simulation(8, 8); sim.Agents.Clear();
        for (int i = 0; i < 3; i++) sim.Agents.Add(new(100 + i, new(3, 3), new Genome(.1f, 1, 2, .1f, 0, 1, 0)));
        sim.Step(1); Assert.Single(sim.Tribes);
        sim.ApplyTool(GodTool.Erase, new(3, 3), 8, 1);
        var loaded = Simulation.LoadJson(sim.SaveJson());
        Assert.Empty(loaded.Agents);
        loaded.Step(1); Assert.Empty(loaded.Tribes);
    }

    [Fact]
    public void SaveRejectsMalformedBoundsBeforeConstructingWorld()
    {
        Assert.Throws<ArgumentException>(() => Simulation.LoadJson("{\"Version\":1,\"Width\":2147483647,\"Height\":2147483647}"));
        var sim = new Simulation(4, 4); var data = JsonSerializer.Deserialize<WorldSave>(sim.SaveJson())!;
        data.Environment[0] = data.Environment[0] with { Fire = 2 };
        Assert.Throws<ArgumentException>(() => Simulation.LoadJson(JsonSerializer.Serialize(data)));
    }
}

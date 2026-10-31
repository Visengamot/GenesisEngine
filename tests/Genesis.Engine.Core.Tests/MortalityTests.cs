using System.Text.Json;
using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class MortalityTests
{
    [Fact]
    public void TwoSeedEcologyHasSustainedTurnoverWithoutPermanentPopulationSaturation()
    {
        foreach (int seed in new[] { 42, 73 })
        {
            var sim = new Simulation(160, 90, seed);
            int saturatedTicks = 0;
            for (int tick = 1; tick <= 120 * 30; tick++)
            {
                sim.Step(1f / 30);
                if (sim.Agents.Count == Simulation.MaximumPopulation) saturatedTicks++;
                if (tick == 60 * 30 || tick == 120 * 30)
                    Assert.InRange(sim.Agents.Count, 200, 900);
            }
            Assert.True(saturatedTicks < 30, $"Seed {seed} stayed at the population ceiling for {saturatedTicks} ticks.");
            Assert.True(sim.LifetimeDeaths >= 100, $"Seed {seed}: only {sim.LifetimeDeaths} natural deaths.");
            Assert.True(sim.Agents.Max(a => a.Generation) >= 5, $"Seed {seed}: evolution stopped.");
            var save = JsonSerializer.Deserialize<WorldSave>(sim.SaveJson())!;
            Assert.True(save.NextId - 80 > sim.LifetimeDeaths, $"Seed {seed}: no sustained births.");
            Assert.Equal(sim.LifetimeDeaths, Simulation.LoadJson(sim.SaveJson()).LifetimeDeaths);
        }
    }
    private static readonly Genome Genome = new(.1f, .25f, 2, .1f, 0, 0, 0);

    private static Simulation Isolated(float energy, float age = 0)
    {
        var sim = new Simulation(5, 5, 12);
        sim.Agents.Clear();
        sim.Agents.Add(new Agent(100, new Vec2(2, 2), Genome) { Energy = energy });
        if (age > 0)
        {
            var save = JsonSerializer.Deserialize<WorldSave>(sim.SaveJson())!;
            save.Agents[0].Age = age;
            sim = Simulation.LoadJson(JsonSerializer.Serialize(save));
        }
        return sim;
    }

    private static void AssertDeath(Simulation sim, string category)
    {
        for (int i = 0; i < 3 && sim.Agents.Count > 0; i++) sim.Step(1);
        Assert.Empty(sim.Agents);
        Assert.Equal(1, sim.LifetimeDeaths);
        var m = sim.Mortality;
        Assert.Equal(1, m.OldAge + m.EnergyDepletion + m.Fire + m.Predators + m.Climate + m.Other);
        Assert.Equal(1, (long)typeof(MortalityStatistics).GetProperty(category)!.GetValue(m)!);
        sim.Step(1);
        Assert.Equal(1, sim.LifetimeDeaths);
    }

    [Fact]
    public void OldAgeOverridesSimultaneousExposureAndAlreadyDeadEnergyIsNotStarvation()
    {
        var old = Isolated(.01f, Simulation.MaximumAge);
        old.ApplyTool(GodTool.Fire, new(2, 2), .5f, 1);
        AssertDeath(old, nameof(MortalityStatistics.OldAge));
        AssertDeath(Isolated(0), nameof(MortalityStatistics.Other));
    }

    [Fact]
    public void EnergyFirePredatorsAndClimateHaveDistinctLethalCauses()
    {
        var hungry = Isolated(.00001f);
        hungry.ApplyTool(GodTool.Dry, new(2, 2), 100, 100);
        for (int y = 0; y < 5; y++) for (int x = 0; x < 5; x++)
        {
            hungry.Food.Set(x, y, 0);
            hungry.Terrain.Set(x, y, 100); // Severe soil exhaustion keeps renewal below metabolism.
        }
        AssertDeath(hungry, nameof(MortalityStatistics.EnergyDepletion));

        var burning = Isolated(.03f);
        burning.ApplyTool(GodTool.Fire, new(2, 2), .5f, 1);
        AssertDeath(burning, nameof(MortalityStatistics.Fire));

        var prey = Isolated(.03f);
        prey.ApplyTool(GodTool.Predator, new(2, 2), 1, 1);
        AssertDeath(prey, nameof(MortalityStatistics.Predators));

        var frozen = Isolated(.01f);
        frozen.ApplyTool(GodTool.Cool, new(2, 2), .5f, 100);
        AssertDeath(frozen, nameof(MortalityStatistics.Climate));
    }

    [Fact]
    public void SaveV5ValidatesAndReplaysCountersWhileLegacySnapshotsStartAtZero()
    {
        var original = Isolated(.1f);
        original.ApplyTool(GodTool.Fire, new(2, 2), .5f, 1);
        original.Step(1);
        string snapshot = original.SaveJson();
        var data = JsonSerializer.Deserialize<WorldSave>(snapshot)!;
        Assert.Equal(6, data.Version);
        Assert.Equal(original.Mortality, data.Mortality);
        var restored = Simulation.LoadJson(snapshot);
        Assert.Equal(snapshot, restored.SaveJson());
        for (int i = 0; i < 5; i++) { original.Step(.25f); restored.Step(.25f); }
        Assert.Equal(original.SaveJson(), restored.SaveJson());
        data.Mortality = data.Mortality!.Value with { Fire = 2 };
        Assert.Throws<ArgumentException>(() => Simulation.LoadJson(JsonSerializer.Serialize(data)));
        data.Mortality = null;
        Assert.Throws<ArgumentException>(() => Simulation.LoadJson(JsonSerializer.Serialize(data)));
        for (int version = 1; version <= 4; version++)
        {
            data.Version = version;
            var legacy = Simulation.LoadJson(JsonSerializer.Serialize(data));
            Assert.Equal(default, legacy.Mortality);
            Assert.Equal(6, JsonSerializer.Deserialize<WorldSave>(legacy.SaveJson())!.Version);
        }
    }

    [Fact]
    public void SeededWorldNaturallyHasBirthsAndDeathsWithoutManualKilling()
    {
        var sim = new Simulation(160, 90, 42);
        for (int tick = 0; tick < 90 * 30; tick++) sim.Step(1f / 30);
        using var save = JsonDocument.Parse(sim.SaveJson());
        long born = save.RootElement.GetProperty("NextId").GetInt64() - 80;
        Assert.True(born > 80, $"Births: {born}");
        Assert.True(sim.LifetimeDeaths > 0, $"Deaths: {sim.LifetimeDeaths}");
        Assert.InRange(sim.Agents.Count, 1, Simulation.MaximumPopulation);
        Assert.Equal(sim.LifetimeDeaths, sim.Mortality.OldAge + sim.Mortality.EnergyDepletion +
            sim.Mortality.Fire + sim.Mortality.Predators + sim.Mortality.Climate + sim.Mortality.Other);
    }
}

using System.Text.Json;
using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class LineageTests
{
    private static readonly Genome Traits = new(1, 1, 2, 1, .5f, .5f, .5f);

    private static NeuralNetwork Brain(float weight)
    {
        var weights = new float[2, Agent.SensorCount];
        for (int o = 0; o < 2; o++)
            for (int i = 0; i < Agent.SensorCount; i++) weights[o, i] = weight;
        return new NeuralNetwork(weights);
    }

    private static Simulation Empty()
    {
        var sim = new Simulation(1, 1, 89);
        sim.Agents.Clear();
        sim.RefreshSpecies();
        return sim;
    }

    [Fact]
    public void FounderDeathKeepsLineageAndRepresentativeSnapshot()
    {
        var sim = Empty();
        var founder = new Agent(100, default, Traits, Brain(0));
        var survivor = new Agent(101, default, Traits, Brain(.1f));
        sim.Agents.AddRange([survivor, founder]);
        sim.RefreshSpecies();
        Assert.Equal(100, survivor.SpeciesId);
        sim.Agents.Remove(founder);
        sim.RefreshSpecies();
        Assert.Equal(100, survivor.SpeciesId);
        Assert.Equal(new SpeciesSummary(100, 1), Assert.Single(sim.Species));
        // The survivor would accept a 1.7-weight newcomer; the founding snapshot does not.
        var divergent = new Agent(102, default, Traits, Brain(1.7f));
        sim.Agents.Add(divergent);
        sim.RefreshSpecies();
        Assert.NotEqual(100, divergent.SpeciesId);
        Assert.Equal(2, sim.SpeciesCount);
    }

    [Fact]
    public void SplitExtinctionAndSaveLoadContinueWithMonotonicDeterministicIds()
    {
        var sim = Empty();
        var founder = new Agent(100, default, Traits, Brain(0));
        var compatible = new Agent(101, default, Traits, Brain(.1f));
        var split = new Agent(102, default, Traits, Brain(4));
        sim.Agents.AddRange([split, compatible, founder]);
        sim.RefreshSpecies();
        Assert.Equal(100, compatible.SpeciesId);
        Assert.Equal(102, split.SpeciesId);
        sim.Agents.Remove(founder);
        sim.RefreshSpecies();
        string saved = sim.SaveJson();
        var loaded = Simulation.LoadJson(saved);
        Assert.Equal(saved, loaded.SaveJson());
        Assert.Equal(sim.Species.ToArray(), loaded.Species.ToArray());
        foreach (var world in new[] { sim, loaded })
        {
            world.Agents.RemoveAll(a => a.Id == 102);
            world.RefreshSpecies();
            Assert.Equal(new SpeciesSummary(100, 1), Assert.Single(world.Species));
            world.Agents.Add(new Agent(103, default, Traits, Brain(4)));
            world.RefreshSpecies();
            Assert.Equal(103, world.Agents.Single(a => a.Id == 103).SpeciesId);
        }
        Assert.Equal(sim.SaveJson(), loaded.SaveJson());
        for (int i = 0; i < 3; i++) { sim.Step(.01f); loaded.Step(.01f); }
        Assert.Equal(sim.SaveJson(), loaded.SaveJson());
    }

    [Fact]
    public void VersionThreeWithoutLineagesReconstructsGroupsAndUpgradesToFour()
    {
        var sim = Empty();
        sim.Agents.AddRange([new Agent(100, default, Traits, Brain(0)),
            new Agent(101, default, Traits, Brain(.1f)), new Agent(102, default, Traits, Brain(4))]);
        var legacy = JsonSerializer.Deserialize<WorldSave>(sim.SaveJson())!;
        legacy.Version = 3;
        legacy.Lineages = null;
        legacy.NextSpeciesId = 0;
        var loaded = Simulation.LoadJson(JsonSerializer.Serialize(legacy));
        Assert.Equal(100, loaded.Agents.Single(a => a.Id == 101).SpeciesId);
        Assert.Equal(102, loaded.Agents.Single(a => a.Id == 102).SpeciesId);
        Assert.Equal(5, JsonSerializer.Deserialize<WorldSave>(loaded.SaveJson())!.Version);
    }

    [Fact]
    public void ExtinctLineagesDoNotAccumulateInSnapshots()
    {
        var sim = Empty();
        for (int i = 0; i < 20; i++)
        {
            sim.Agents.Add(new Agent(100 + i, default, Traits, Brain(4)));
            sim.RefreshSpecies();
            Assert.Single(sim.Species);
            sim.Agents.Clear();
            sim.RefreshSpecies();
            Assert.Empty(JsonSerializer.Deserialize<WorldSave>(sim.SaveJson())!.Lineages!);
        }
    }
}

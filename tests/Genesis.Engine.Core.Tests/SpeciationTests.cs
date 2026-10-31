using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class SpeciationTests
{
    private static readonly Genome Traits = new(1, 1, 2, 1, .5f, .5f, .5f);
    private static NeuralNetwork Brain(float weight = 0)
    {
        var weights = new float[2, Agent.SensorCount];
        for (int o = 0; o < 2; o++) for (int i = 0; i < Agent.SensorCount; i++) weights[o, i] = weight;
        return new(weights);
    }

    [Fact]
    public void MatchingWeightsAndDisjointExcessInnovationsHaveExactSymmetricDistance()
    {
        var template = new NeuralNetwork(new float[1, 3]).ToSnapshot();
        var a = new NeuralNetwork(template with { Connections = [template.Connections[0] with { Weight = 1 }, template.Connections[2]] });
        var b = new NeuralNetwork(template with { Connections = [template.Connections[0] with { Weight = -1, Enabled = false }, template.Connections[1]] });
        var result = GeneticCompatibility.Compare(a, b);
        Assert.Equal(1, result.Matching);
        Assert.Equal(1, result.Disjoint);
        Assert.Equal(1, result.Excess);
        Assert.Equal(2, result.MeanWeightDifference);
        Assert.Equal(1.8, result.Distance, 10);
        Assert.Equal(result, GeneticCompatibility.Compare(b, a));
        Assert.Equal(0, GeneticCompatibility.Distance(a, a));
        Assert.Equal(.8, GeneticCompatibility.Distance(Brain(1), Brain(-1)), 10);
        var empty = new NeuralNetwork(template with { Connections = [] });
        Assert.Equal(0, GeneticCompatibility.Distance(empty, empty));
        Assert.Equal(1, GeneticCompatibility.Distance(empty, a));
        Assert.True(double.IsPositiveInfinity(GeneticCompatibility.Distance(a, Brain())));
    }

    [Fact]
    public void ClassificationSeparatesGeneticsAndIsIndependentOfListOrder()
    {
        var sim = new Simulation(1, 1); sim.Agents.Clear();
        var a = new Agent(100, default, Traits, Brain());
        var b = new Agent(101, default, Traits, Brain(.1f));
        var c = new Agent(102, default, Traits, Brain(4));
        sim.Agents.AddRange([c, b, a]); sim.RefreshSpecies();
        Assert.Equal(2, sim.SpeciesCount);
        Assert.Equal(a.SpeciesId, b.SpeciesId);
        Assert.NotEqual(a.SpeciesId, c.SpeciesId);
        Assert.Equal(100, a.SpeciesId);
        var groups = sim.Species.ToArray();
        sim.Agents.Reverse(); sim.RefreshSpecies();
        Assert.Equal(groups, sim.Species.ToArray());
        sim.Agents.Clear(); sim.RefreshSpecies();
        Assert.Equal(0, sim.SpeciesCount);
    }

    [Fact]
    public void StructuralDifferencesAloneSeparateSpecies()
    {
        var dense = Brain();
        var snapshot = dense.ToSnapshot();
        var sparse = new NeuralNetwork(snapshot with { Connections = [snapshot.Connections[0]] });
        var sim = new Simulation(1, 1); sim.Agents.Clear();
        sim.Agents.AddRange([new(100, default, Traits, dense), new(101, default, Traits, sparse)]);
        sim.RefreshSpecies();
        Assert.Equal(2, sim.SpeciesCount);
        Assert.Equal(0, GeneticCompatibility.Compare(dense, sparse).MeanWeightDifference);
    }

    private static Simulation Mature(params Agent[] agents)
    {
        var sim = new Simulation(1, 1); sim.Agents.Clear(); sim.Agents.AddRange(agents);
        for (int i = 0; i < 5; i++)
        {
            foreach (var a in agents) a.Energy = 1;
            sim.Step(1);
        }
        foreach (var a in agents) a.Energy = 2;
        return sim;
    }

    [Fact]
    public void ReproductionPrefersCompatibleMateEvenWhenOtherSpeciesRanksFirst()
    {
        var a = new Agent(100, default, Traits, Brain());
        var incompatible = new Agent(101, default, Traits, Brain(4));
        var compatible = new Agent(102, default, Traits, Brain());
        var sim = Mature(a, incompatible, compatible);
        sim.Step(.01f);
        Assert.Equal(1, a.OffspringCount);
        Assert.Equal(0, incompatible.OffspringCount);
        Assert.Equal(1, compatible.OffspringCount);
        Assert.True(a.Energy < 1.5f && compatible.Energy < 1.5f);
        var child = Assert.Single(sim.Agents.Where(x => x.Generation == 1));
        Assert.Equal(a.SpeciesId, child.SpeciesId);
        Assert.Equal(4, sim.Species.Sum(s => s.MemberCount));
    }

    [Fact]
    public void IsolatedSpeciesDoNotMateUnlessFallbackIsExplicitlyEnabled()
    {
        var a = new Agent(100, default, Traits, Brain());
        var b = new Agent(101, default, Traits, Brain(4));
        var sim = Mature(a, b);
        sim.Step(.01f);
        Assert.Equal(2, sim.Agents.Count);
        sim.AllowInterspeciesMating = true;
        sim.Step(.01f);
        Assert.Equal(3, sim.Agents.Count);
        Assert.Equal(1, a.OffspringCount);
        Assert.Equal(1, b.OffspringCount);
    }

    [Fact]
    public void SavePreservesSelectionStateAndOldSnapshotsDefaultSafely()
    {
        var a = new Agent(100, default, Traits, Brain());
        var b = new Agent(101, default, Traits, Brain());
        var sim = Mature(a, b);
        sim.AllowInterspeciesMating = true;
        sim.Step(.01f);
        var loaded = Simulation.LoadJson(sim.SaveJson());
        Assert.True(loaded.AllowInterspeciesMating);
        Assert.Equal(sim.Species.ToArray(), loaded.Species.ToArray());
        Assert.Equal(sim.Agents.Select(x => x.OffspringCount), loaded.Agents.Select(x => x.OffspringCount));
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(sim.SaveJson())!.AsObject();
        legacy.Remove("AllowInterspeciesMating");
        foreach (var agent in legacy["Agents"]!.AsArray()) agent!.AsObject().Remove("OffspringCount");
        loaded = Simulation.LoadJson(legacy.ToJsonString());
        Assert.False(loaded.AllowInterspeciesMating);
        Assert.All(loaded.Agents, agent => Assert.Equal(0, agent.OffspringCount));
        legacy["Agents"]![0]!["OffspringCount"] = -1;
        Assert.Throws<ArgumentException>(() => Simulation.LoadJson(legacy.ToJsonString()));
    }

    [Fact]
    public void OnlineSelectionPrefersEnergyOverIdentifierAndCountsPriorOffspring()
    {
        var low = new Agent(100, default, Traits, Brain());
        var high = new Agent(101, default, Traits, Brain());
        var middle = new Agent(102, default, Traits, Brain());
        var sim = Mature(low, high, middle);
        low.Energy = 1.6f; high.Energy = 2.8f; middle.Energy = 2;
        Assert.True(Simulation.ReproductiveFitness(high) > Simulation.ReproductiveFitness(low));
        sim.Step(.01f);
        Assert.Equal(0, low.OffspringCount);
        Assert.Equal(1, high.OffspringCount);
        Assert.Equal(1, middle.OffspringCount);
        high.Energy = low.Energy;
        Assert.True(Simulation.ReproductiveFitness(high) > Simulation.ReproductiveFitness(low));
        Assert.True(Simulation.ReproductiveFitness(low) > Simulation.ReproductiveFitness(new Agent(999, default, Traits, Brain()) { Energy = low.Energy }));
    }
}

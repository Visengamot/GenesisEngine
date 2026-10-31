using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class EvolutionTests
{
    private static readonly Genome Traits = new(1, 1, 2, 1, .5f, .5f, .5f);
    private static NeuralNetwork StillBrain() => new(new float[2, Agent.SensorCount]);

    [Fact]
    public void SeedReplaysAgentsFoodAndBrains()
    {
        var a = new Simulation(12, 9, 17);
        var b = new Simulation(12, 9, 17);
        for (int i = 0; i < 300; i++) { a.Step(.05f); b.Step(.05f); }
        Assert.Equal(a.Tick, b.Tick);
        Assert.Equal(a.Agents.Count, b.Agents.Count);
        for (int i = 0; i < a.Agents.Count; i++)
        {
            Assert.Equal(a.Agents[i].Id, b.Agents[i].Id);
            Assert.Equal(a.Agents[i].Position, b.Agents[i].Position);
            Assert.Equal(a.Agents[i].Energy, b.Agents[i].Energy);
            Assert.Equal(a.Agents[i].Genome, b.Agents[i].Genome);
        }
        for (int y = 0; y < 9; y++)
        for (int x = 0; x < 12; x++) Assert.Equal(a.Food.Get(x, y), b.Food.Get(x, y));
    }

    [Fact]
    public void ProceduralTerrainIsSeededBoundedAndBlankMapsStayBlank()
    {
        var a = new Simulation(40, 30, 10);
        var b = new Simulation(40, 30, 10);
        var c = new Simulation(40, 30, 11);
        Assert.Equal(0, new TerrainMap(2, 2).Get(1, 1));
        Assert.NotEqual(a.Terrain.Get(0, 0), c.Terrain.Get(0, 0));
        for (int y = 0; y < 30; y++)
        for (int x = 0; x < 40; x++)
        {
            Assert.Equal(a.Terrain.Get(x, y), b.Terrain.Get(x, y));
            Assert.InRange(a.Terrain.Get(x, y), -.5f, 1);
        }
    }

    [Fact]
    public void ArgumentsRejectInvalidDimensionsStepsAndNetworkInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerrainMap(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerrainBrush(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerrainBrush(float.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NeuralNetwork(0, 2));
        Assert.Throws<ArgumentException>(() => new NeuralNetwork(2, 2).Evaluate(new float[1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NeuralNetwork(1, 1).Evaluate(new[] { float.NaN }));
        var simulation = new Simulation(4, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Step(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Step(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Step(2));
        simulation.Step(0);
        Assert.Equal(0, simulation.Tick);
    }

    [Fact]
    public void FoodIsConsumedAndRenewsWithoutAgents()
    {
        var simulation = new Simulation(5, 5);
        simulation.Agents.Clear();
        simulation.Food.Set(2, 2, 1);
        var agent = new Agent(100, new(2, 2), Traits, StillBrain());
        simulation.Agents.Add(agent);
        simulation.Step(.5f);
        Assert.True(simulation.Food.Get(2, 2) < 1);
        Assert.True(agent.Energy > 1);
        float remaining = simulation.Food.Get(2, 2);
        simulation.Agents.Clear();
        simulation.Step(.5f);
        Assert.True(simulation.Food.Get(2, 2) > remaining);
        for (int i = 0; i < 100; i++) simulation.Step(1);
        Assert.InRange(simulation.Food.Get(2, 2), 0, 1);
    }

    [Fact]
    public void BrainUsesFoodAndTerrainSensorsToMove()
    {
        var simulation = new Simulation(10, 10);
        simulation.Agents.Clear();
        var weights = new float[2, Agent.SensorCount];
        weights[0, 0] = 1; // East food drives eastward motion.
        var agent = new Agent(100, new(4, 4), Traits, new(weights));
        simulation.Agents.Add(agent);
        simulation.Food.Set(6, 4, 0);
        Assert.Equal(0, simulation.Sense(agent)[0]);
        simulation.Food.Set(6, 4, 1);
        simulation.Terrain.Set(6, 4, 2);
        Assert.Equal(1, simulation.Sense(agent)[0]);
        Assert.True(simulation.Sense(agent)[4] > .9f);
        simulation.Step(.1f);
        Assert.True(agent.Position.X > 4);
        Assert.Equal(4, agent.Position.Y);
    }

    [Fact]
    public void GenomesAndInheritedWeightsRemainBoundedAndIndependent()
    {
        var random = new Random(19);
        var genome = new Genome(-10, 100, 100, -10, 2, -1, 2);
        var a = new float[2, Agent.SensorCount];
        var b = new float[2, Agent.SensorCount];
        for (int o = 0; o < 2; o++)
        for (int i = 0; i < Agent.SensorCount; i++) { a[o, i] = -4; b[o, i] = 4; }
        var parent = new NeuralNetwork(a);
        var other = new NeuralNetwork(b);
        var inherited = parent.Crossover(other, random, 0).GetWeights();
        Assert.Contains(inherited.Cast<float>(), value => value == -4);
        Assert.Contains(inherited.Cast<float>(), value => value == 4);
        for (int n = 0; n < 1000; n++)
        {
            genome = genome.Crossover(Traits, random).Mutate(random);
            Assert.InRange(genome.Speed, .1f, 3);
            Assert.InRange(genome.Size, .25f, 3);
            Assert.InRange(genome.Vision, 1, 12);
            Assert.InRange(genome.Metabolism, .1f, 3);
            Assert.InRange(genome.Aggression, 0, 1);
            Assert.InRange(genome.Sociality, 0, 1);
            Assert.InRange(genome.Curiosity, 0, 1);
            parent = parent.Crossover(other, random, 1);
            Assert.All(parent.GetWeights().Cast<float>(), value => Assert.InRange(value, -4, 4));
        }
        var copy = parent.GetWeights();
        copy[0, 0] = 100;
        Assert.InRange(parent.GetWeights()[0, 0], -4, 4);
    }

    [Fact]
    public void BirthCostsEnergyAndIdsAreNeverReusedAfterDeath()
    {
        var simulation = new Simulation(8, 8);
        simulation.Agents.Clear();
        var first = new Agent(200, new(3, 3), Traits, StillBrain());
        var second = new Agent(300, new(3, 3), Traits, StillBrain());
        simulation.Agents.AddRange([first, second]);
        for (int i = 0; i < 50; i++) simulation.Step(.1f);
        first.Energy = 2;
        second.Energy = 2;
        simulation.Step(.1f);
        var child = Assert.Single(simulation.Agents.Where(a => a.Generation == 1));
        Assert.True(child.Id > 300);
        Assert.True(first.Energy < 1.5f);
        Assert.True(second.Energy < 1.5f);
        Assert.Equal(1, child.Energy);
        int oldId = child.Id;
        child.Energy = 0;
        first.Energy = 2;
        second.Energy = 2;
        simulation.Step(.1f);
        Assert.DoesNotContain(simulation.Agents, a => a.Id == oldId);
        Assert.Contains(simulation.Agents, a => a.Id > oldId);
        Assert.Equal(simulation.Agents.Count, simulation.Agents.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public void AgentsDieOfAgeAndDuplicateIdsAreRejected()
    {
        var simulation = new Simulation(1, 1);
        simulation.Agents.Clear();
        var agent = new Agent(100, new(0, 0), Traits, StillBrain());
        simulation.Agents.Add(agent);
        for (int i = 0; i < 180; i++) { agent.Energy = 2; simulation.Step(1); }
        Assert.Empty(simulation.Agents);
        simulation.Agents.Add(new(5, new(0, 0), Traits));
        simulation.Agents.Add(new(5, new(0, 0), Traits));
        Assert.Throws<InvalidOperationException>(() => simulation.Step(.1f));
    }
}

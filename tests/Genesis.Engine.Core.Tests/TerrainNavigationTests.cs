using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class TerrainNavigationTests
{
    private static readonly Genome Traits = new(1, 1, 2, 1, .5f, .5f, .5f);

    private static (Simulation Simulation, Agent Agent) MovingAgent(Vec2 start, Vec2 heading)
    {
        var sim = new Simulation(16, 12, 81);
        sim.Agents.Clear();
        var agent = new Agent(1000, start, Traits, new NeuralNetwork(new float[2, Agent.SensorCount]))
        {
            Heading = heading
        };
        sim.Agents.Add(agent);
        return (sim, agent);
    }

    [Theory]
    [InlineData(-.7f)]
    [InlineData(3.1f)]
    public void ShorelineAndPeakReflectWithoutEnteringObstacle(float height)
    {
        var (sim, agent) = MovingAgent(new(4.8f, 5), new(1, 0));
        sim.Terrain.Set(5, 5, height);
        Assert.Equal(1, sim.Sense(agent)[4]);
        for (int i = 0; i < 8; i++)
        {
            sim.Step(.1f);
            Assert.True(agent.Position.X < 5, $"Entered blocked cell at {agent.Position}");
            Assert.Equal(5, agent.Position.Y);
        }
        Assert.True(agent.Heading.X < 0);
    }

    [Fact]
    public void BrushIntroducesDeepWaterAndMountainBlockers()
    {
        var (sim, agent) = MovingAgent(new(3.8f, 5), new(1, 0));
        new TerrainBrush(.9f, -2).Apply(sim.Terrain, new(4, 5));
        Assert.True(sim.Terrain.Get(4, 5) <= Simulation.DeepWaterLevel);
        Assert.Equal(1, sim.Sense(agent)[4]);
        sim.Step(.1f);
        Assert.True(agent.Position.X < 4);
        sim.Terrain.Set(4, 5, 0);
        new TerrainBrush(.9f, 5).Apply(sim.Terrain, new(4, 5));
        Assert.True(sim.Terrain.Get(4, 5) >= Simulation.MountainLevel);
        Assert.Equal(1, sim.Sense(agent)[4]);
    }

    [Fact]
    public void LongStepDoesNotTunnelAcrossThinWaterOrPeak()
    {
        foreach (float obstacle in new[] { -1f, 4f })
        {
            var (sim, agent) = MovingAgent(new(3.2f, 5), new(1, 0));
            var weights = new float[2, Agent.SensorCount];
            weights[0, 11] = 4; // Force a full-speed eastward attempt over multiple cells.
            sim.Agents.Clear();
            agent = new Agent(1000, new(3.2f, 5), new Genome(3, 1, 2, 1, .5f, .5f, .5f), new NeuralNetwork(weights))
            { Heading = new(1, 0) };
            sim.Agents.Add(agent);
            sim.Terrain.Set(5, 5, obstacle);
            sim.Step(.5f);
            Assert.True(agent.Position.X < 5, $"Tunneled through obstacle {obstacle}: {agent.Position}");
        }
    }

    [Fact]
    public void DiagonalMovementCannotCutBlockedCorner()
    {
        var (sim, agent) = MovingAgent(new(4.8f, 4.8f), new(.7f, .7f));
        sim.Terrain.Set(5, 5, -1);
        sim.Step(.5f);
        Assert.False((int)agent.Position.X == 5 && (int)agent.Position.Y == 5);
    }

    [Fact]
    public void ModerateHillRemainsPassableForEastFoodSensor()
    {
        var (sim, agent) = MovingAgent(new(4, 4), default);
        var weights = new float[2, Agent.SensorCount];
        weights[0, 0] = 1;
        sim.Agents.Clear();
        agent = new Agent(1000, new(4, 4), Traits, new NeuralNetwork(weights));
        sim.Agents.Add(agent);
        sim.Food.Set(6, 4, 1);
        sim.Terrain.Set(6, 4, 2);
        Assert.True(sim.Sense(agent)[4] > .9f);
        sim.Step(.1f);
        Assert.True(agent.Position.X > 4);
    }

    [Fact]
    public void ReflectionsStayDeterministicAndBoundaryDistributionIsRetained()
    {
        var a = new Simulation(40, 30, 193);
        var b = new Simulation(40, 30, 193);
        foreach (var sim in new[] { a, b })
            for (int y = 4; y < 26; y++) sim.Terrain.Set(20, y, -1);
        for (int i = 0; i < 100; i++) { a.Step(.05f); b.Step(.05f); }
        Assert.Equal(a.Agents.Select(agent => agent.Position), b.Agents.Select(agent => agent.Position));
        Assert.All(a.Agents, agent =>
        {
            Assert.InRange(agent.Position.X, 0, 39);
            Assert.InRange(agent.Position.Y, 0, 29);
        });
    }
}

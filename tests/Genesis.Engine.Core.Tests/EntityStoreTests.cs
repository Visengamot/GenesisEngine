using Genesis.Engine.Core;
namespace Genesis.Engine.Core.Tests;

public class EntityStoreTests
{
    private static Agent Make(int id, float x, float y) => new(id, new(x, y), new Genome(1, 1, 2, 1, 0, 0, 0));
    [Fact]
    public void NearestMatchesBruteForceAndChecksFewerCandidates()
    {
        var random = new Random(17);
        var agents = Enumerable.Range(0, 600).Select(i => Make(i, random.NextSingle() * 160, random.NextSingle() * 90)).ToList();
        var store = new AgentStore(); store.Rebuild(agents);
        for (int i = 0; i < 100; i++)
        {
            var p = new Vec2(random.NextSingle() * 180 - 10, random.NextSingle() * 110 - 10);
            double Distance(Agent a) { double x = (double)a.Position.X - p.X, y = (double)a.Position.Y - p.Y; return x * x + y * y; }
            Assert.Same(agents.MinBy(Distance), store.Nearest(p));
        }
        Assert.True(store.CandidateChecks < 6000);
    }
    [Fact]
    public void TiesKeepListOrderAndEnergyIsLive()
    {
        var first = Make(9, -1, 0); var second = Make(1, 1, 0);
        var store = new AgentStore(); store.Rebuild([first, second]);
        Assert.Same(first, store.Nearest(default));
        first.Energy = 0;
        Assert.Same(second, store.Nearest(default, a => a.Energy > 0));
        Assert.Equal(new[] { first, second }, store.QueryBox(default, 1));
        store.Rebuild([]); Assert.Null(store.Nearest(default));
    }
    [Fact]
    public void RebuildTracksMovementRemovalAndDuplicateIds()
    {
        var a = Make(1, 0, 0); var b = Make(2, 20, 20);
        var store = new AgentStore(); store.Rebuild([a, b]);
        a.Position = new(100, 100); store.Rebuild([a, b]);
        Assert.Same(b, store.Nearest(default));
        store.Rebuild([a]); Assert.Equal(1, store.Count);
        Assert.Throws<InvalidOperationException>(() => store.Rebuild([a, a]));
    }
    [Fact]
    public void SimulationReconcilesDirectListEditsBeforePredators()
    {
        var sim = new Simulation(20, 20); sim.Agents.Clear();
        var a = Make(100, 3, 3); sim.Agents.Add(a);
        sim.ApplyTool(GodTool.Predator, new(3, 3), 1, 1); sim.Step(.1f);
        Assert.Equal(1, sim.Diagnostics.SpatialBucketCount);
        Assert.True(sim.Diagnostics.SpatialCandidateChecks > 0);
        sim.Agents.Clear(); sim.Step(.1f);
        Assert.Equal(0, sim.Diagnostics.SpatialBucketCount);
    }
}

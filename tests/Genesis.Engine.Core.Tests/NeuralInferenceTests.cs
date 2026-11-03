using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public class NeuralInferenceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(193)]
    public void SpanEvaluationMatchesLegacyAndWrapperOnEvolvedGraphs(int seed)
    {
        var random = new Random(seed);
        var network = new NeuralNetwork(5, 3, seed);
        for (int generation = 0; generation < 20; generation++)
        {
            network = network.MutateTopology(random, 1, 1);
            if (generation % 4 == 0)
                network = network.Crossover(new NeuralNetwork(5, 3, seed + generation), random, .5f);
            var snapshot = network.ToSnapshot();
            // Storage order must not be mistaken for node index or topological order.
            network = new NeuralNetwork(snapshot with { Nodes = snapshot.Nodes.Reverse().ToArray() });
            Assert.Equal(snapshot.Nodes.Length, network.ScratchSize);
            var scratch = Enumerable.Repeat(float.NaN, network.ScratchSize + 2).ToArray();
            var outputs = Enumerable.Repeat(123f, network.OutputCount + 2).ToArray();
            for (int sample = 0; sample < 8; sample++)
            {
                var input = Enumerable.Range(0, network.InputCount).Select(_ => random.NextSingle() * 4 - 2).ToArray();
                var expected = LegacyEvaluate(network.ToSnapshot(), input);
                network.Evaluate(input, outputs, scratch);
                Assert.Equal(expected, outputs.Take(network.OutputCount).ToArray());
                Assert.Equal(expected, network.Evaluate(input));
                Assert.All(outputs.Skip(network.OutputCount), value => Assert.Equal(123f, value));
                Assert.All(scratch.Skip(network.ScratchSize), value => Assert.True(float.IsNaN(value)));
            }
        }
        Assert.True(network.HiddenNodeCount > 0);
        Assert.Contains(network.ToSnapshot().Connections, c => !c.Enabled);
    }

    [Fact]
    public void SpanEvaluationValidatesBufferSizes()
    {
        var network = new NeuralNetwork(3, 2);
        var input = new float[3]; var outputs = new float[2]; var scratch = new float[network.ScratchSize];
        Assert.Equal("input", Assert.Throws<ArgumentException>(() => network.Evaluate(new float[2], outputs, scratch)).ParamName);
        Assert.Equal("input", Assert.Throws<ArgumentException>(() => network.Evaluate(new float[4], outputs, scratch)).ParamName);
        Assert.Equal("outputs", Assert.Throws<ArgumentException>(() => network.Evaluate(input, new float[1], scratch)).ParamName);
        Assert.Equal("scratch", Assert.Throws<ArgumentException>(() => network.Evaluate(input, outputs, new float[network.ScratchSize - 1])).ParamName);
        Assert.Throws<ArgumentException>(() => network.Evaluate(input, Span<float>.Empty, scratch));
        Assert.Throws<ArgumentException>(() => network.Evaluate(input, outputs, Span<float>.Empty));
        Assert.Throws<ArgumentException>(() => network.Evaluate(new float[2]));
        network.Evaluate(input, outputs, scratch);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void BothOverloadsRejectNonFiniteInputWithoutWritingBuffers(float invalid)
    {
        var network = new NeuralNetwork(3, 2);
        var input = new[] { 1f, invalid, 0f };
        var outputs = Enumerable.Repeat(123f, network.OutputCount).ToArray();
        var scratch = Enumerable.Repeat(456f, network.ScratchSize).ToArray();
        Assert.Equal("input", Assert.Throws<ArgumentOutOfRangeException>(() => network.Evaluate(input, outputs, scratch)).ParamName);
        Assert.Equal("input", Assert.Throws<ArgumentOutOfRangeException>(() => network.Evaluate(input)).ParamName);
        Assert.All(outputs, value => Assert.Equal(123f, value));
        Assert.All(scratch, value => Assert.Equal(456f, value));
    }

    [Fact]
    public void RepeatedSpanEvaluationAllocatesZeroBytesAfterWarmup()
    {
        var random = new Random(42);
        var network = new NeuralNetwork(12, 2, 42);
        for (int i = 0; i < 20; i++) network = network.MutateTopology(random, 1, 1);
        var input = Enumerable.Repeat(.25f, network.InputCount).ToArray();
        var outputs = new float[network.OutputCount]; var scratch = new float[network.ScratchSize];
        for (int i = 0; i < 10_000; i++) network.Evaluate(input, outputs, scratch);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) network.Evaluate(input, outputs, scratch);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0L, allocated);
        Assert.Equal(network.Evaluate(input), outputs);
    }

    // Independent copy of the original evaluation algorithm protects ordering and arithmetic semantics.
    private static float[] LegacyEvaluate(NetworkSnapshot snapshot, float[] input)
    {
        var slots = snapshot.Nodes.Select((node, slot) => (node.Id, slot)).ToDictionary(p => p.Id, p => p.slot);
        var values = new float[snapshot.Nodes.Length];
        foreach (var node in snapshot.Nodes.Where(n => n.Kind == NeuralNodeKind.Input)) values[slots[node.Id]] = input[node.Index];
        foreach (var node in snapshot.Nodes.Where(n => n.Kind != NeuralNodeKind.Input).OrderBy(n => n.Depth).ThenBy(n => n.Id))
        {
            double sum = 0;
            foreach (var connection in snapshot.Connections.Where(c => c.Enabled && c.To == node.Id))
                sum += (double)values[slots[connection.From]] * connection.Weight;
            values[slots[node.Id]] = (float)Math.Tanh(sum);
        }
        var output = new float[snapshot.OutputCount];
        foreach (var node in snapshot.Nodes.Where(n => n.Kind == NeuralNodeKind.Output)) output[node.Index] = values[slots[node.Id]];
        return output;
    }
}

namespace Genesis.Engine.Core;

public enum NeuralNodeKind { Input, Hidden, Output }
public sealed record NodeGene(int Id, NeuralNodeKind Kind, int Index, double Depth);
public sealed record ConnectionGene(long Innovation, int From, int To, float Weight, bool Enabled = true);
public sealed record NetworkSnapshot(int InputCount, int OutputCount, NodeGene[] Nodes, ConnectionGene[] Connections);

/// <summary>NEAT-inspired feed-forward structural evolution with homologous innovations. Online species and reproductive sharing are implemented by Simulation.</summary>
public sealed class NeuralNetwork
{
    private readonly NodeGene[] nodes;
    private readonly ConnectionGene[] connections;
    private readonly Dictionary<int, int> slots;
    private readonly int[] inputSlots;
    private readonly int[] outputSlots;
    private readonly (int Target, (int Source, float Weight)[] Incoming)[] evaluation;
    public int InputCount { get; }
    public int OutputCount { get; }
    /// <summary>Minimum scratch buffer length required by span-based evaluation.</summary>
    public int ScratchSize => nodes.Length;
    public int HiddenNodeCount => nodes.Count(n => n.Kind == NeuralNodeKind.Hidden);
    public int ConnectionCount => connections.Count(c => c.Enabled);

    public NeuralNetwork(int input, int output, int seed = 1) : this(RandomWeights(input, output, seed)) { }
    public NeuralNetwork(float[,] weights) : this(DenseSnapshot(weights)) { }
    public NeuralNetwork(NetworkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateDimensions(snapshot.InputCount, snapshot.OutputCount);
        InputCount = snapshot.InputCount; OutputCount = snapshot.OutputCount;
        if (snapshot.Nodes is null || snapshot.Connections is null || snapshot.Nodes.Length > InputCount + OutputCount + 128 || snapshot.Connections.Length > 1_100_000)
            throw new ArgumentException("Invalid network graph.", nameof(snapshot));
        nodes = snapshot.Nodes.ToArray(); connections = snapshot.Connections.ToArray();
        if (nodes.Any(n => n is null || n.Id < 0 || !double.IsFinite(n.Depth) || !Enum.IsDefined(n.Kind)) || nodes.Select(n => n.Id).Distinct().Count() != nodes.Length)
            throw new ArgumentException("Invalid or duplicate nodes.", nameof(snapshot));
        slots = nodes.Select((n, i) => (n.Id, i)).ToDictionary(p => p.Id, p => p.i);
        foreach (var kind in new[] { NeuralNodeKind.Input, NeuralNodeKind.Output })
        {
            int count = kind == NeuralNodeKind.Input ? InputCount : OutputCount;
            if (!nodes.Where(n => n.Kind == kind).Select(n => n.Index).Order().SequenceEqual(Enumerable.Range(0, count)))
                throw new ArgumentException("Missing input/output nodes.", nameof(snapshot));
        }
        inputSlots = new int[InputCount]; outputSlots = new int[OutputCount];
        for (int slot = 0; slot < nodes.Length; slot++)
        {
            var node = nodes[slot];
            if (node.Kind == NeuralNodeKind.Input) inputSlots[node.Index] = slot;
            else if (node.Kind == NeuralNodeKind.Output) outputSlots[node.Index] = slot;
        }
        var innovations = new HashSet<long>(); var endpoints = new HashSet<(int, int)>();
        foreach (var c in connections)
        {
            if (c is null || !slots.ContainsKey(c.From) || !slots.ContainsKey(c.To) || !innovations.Add(c.Innovation) || !endpoints.Add((c.From, c.To)) || c.Innovation != Innovation(c.From, c.To))
                throw new ArgumentException("Invalid connection innovation.", nameof(snapshot));
            Guard.Finite(c.Weight, nameof(snapshot));
            if (Math.Abs(c.Weight) > 4 || nodes[slots[c.From]].Depth >= nodes[slots[c.To]].Depth || nodes[slots[c.To]].Kind == NeuralNodeKind.Input || nodes[slots[c.From]].Kind == NeuralNodeKind.Output)
                throw new ArgumentException("Connections must form a bounded feed-forward graph.", nameof(snapshot));
        }
        evaluation = nodes.Where(n => n.Kind != NeuralNodeKind.Input).OrderBy(n => n.Depth).ThenBy(n => n.Id)
            .Select(n => (slots[n.Id], connections.Where(c => c.Enabled && c.To == n.Id).Select(c => (slots[c.From], c.Weight)).ToArray())).ToArray();
    }

    private static void ValidateDimensions(int input, int output)
    {
        if (input is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(input));
        if (output is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(output));
    }
    private static float[,] RandomWeights(int input, int output, int seed)
    {
        ValidateDimensions(input, output);
        var weights = new float[output, input]; var random = new Random(seed);
        for (int o = 0; o < output; o++) for (int i = 0; i < input; i++) weights[o, i] = random.NextSingle() * 2 - 1;
        return weights;
    }
    private static NetworkSnapshot DenseSnapshot(float[,] weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        int input = weights.GetLength(1), output = weights.GetLength(0); ValidateDimensions(input, output);
        var nodes = Enumerable.Range(0, input).Select(i => new NodeGene(i, NeuralNodeKind.Input, i, 0))
            .Concat(Enumerable.Range(0, output).Select(o => new NodeGene(input + o, NeuralNodeKind.Output, o, 1))).ToArray();
        var genes = new List<ConnectionGene>();
        for (int o = 0; o < output; o++) for (int i = 0; i < input; i++) genes.Add(new(Innovation(i, input + o), i, input + o, weights[o, i]));
        return new(input, output, nodes, genes.ToArray());
    }
    // Endpoint-derived innovations are deterministic across simulations and snapshot loads.
    private static long Innovation(int from, int to) => ((long)from << 32) | (uint)to;
    private static int SplitNodeId(long innovation)
    {
        ulong hash = (ulong)innovation + 0x9e3779b97f4a7c15UL;
        hash = (hash ^ (hash >> 30)) * 0xbf58476d1ce4e5b9UL;
        hash = (hash ^ (hash >> 27)) * 0x94d049bb133111ebUL;
        return 4096 + (int)((hash ^ (hash >> 31)) % (int.MaxValue - 4096UL));
    }
    public NetworkSnapshot ToSnapshot() => new(InputCount, OutputCount, nodes.ToArray(), connections.ToArray());

    /// <summary>Compatibility view of enabled direct input/output weights. Use ToSnapshot for the complete topology.</summary>
    public float[,] GetWeights()
    {
        var result = new float[OutputCount, InputCount];
        foreach (var c in connections.Where(c => c.Enabled))
        {
            var a = nodes[slots[c.From]]; var b = nodes[slots[c.To]];
            if (a.Kind == NeuralNodeKind.Input && b.Kind == NeuralNodeKind.Output) result[b.Index, a.Index] = c.Weight;
        }
        return result;
    }
    public float[] Evaluate(ReadOnlySpan<float> input)
    {
        var output = new float[OutputCount];
        Evaluate(input, output, new float[ScratchSize]);
        return output;
    }

    /// <summary>Evaluates without allocating, using caller-owned output and scratch buffers.</summary>
    /// <remarks>Input length must match InputCount. Outputs and scratch require at least OutputCount
    /// and ScratchSize elements respectively; their unused tails are untouched. Scratch must not
    /// overlap input or outputs. Scratch contents need not be initialized and are overwritten.</remarks>
    public void Evaluate(ReadOnlySpan<float> input, Span<float> outputs, Span<float> scratch)
    {
        if (input.Length != InputCount) throw new ArgumentException("Input length must match the network.", nameof(input));
        if (outputs.Length < OutputCount) throw new ArgumentException("Output buffer is too small.", nameof(outputs));
        if (scratch.Length < ScratchSize) throw new ArgumentException("Scratch buffer is too small.", nameof(scratch));
        foreach (float value in input) Guard.Finite(value, nameof(input));
        for (int i = 0; i < inputSlots.Length; i++) scratch[inputSlots[i]] = input[i];
        foreach (var (target, incoming) in evaluation)
        {
            double sum = 0; foreach (var (source, weight) in incoming) sum += (double)scratch[source] * weight;
            scratch[target] = (float)Math.Tanh(sum);
        }
        for (int i = 0; i < outputSlots.Length; i++) outputs[i] = scratch[outputSlots[i]];
    }
    public static NeuralNetwork CreateForager(int seed)
    {
        var random = new Random(seed); var weights = new float[2, Agent.SensorCount];
        // Symmetric food gradients avoid a shared world-axis preference. Other sensor weights start small.
        for (int o = 0; o < 2; o++) for (int i = 4; i < Agent.SensorCount; i++) weights[o, i] = (random.NextSingle() - .5f) * .08f;
        float x = .8f + random.NextSingle() * .6f, y = .8f + random.NextSingle() * .6f;
        weights[0, 0] = x; weights[0, 1] = -x; weights[1, 2] = y; weights[1, 3] = -y;
        weights[0, 4] = -.35f; weights[0, 5] = .35f; weights[1, 6] = -.35f; weights[1, 7] = .35f;
        weights[0, 11] = 0; weights[1, 11] = 0;
        return new(weights);
    }
    public NeuralNetwork Crossover(NeuralNetwork other, Random random, float mutationRate = .1f)
    {
        ArgumentNullException.ThrowIfNull(other); ArgumentNullException.ThrowIfNull(random);
        Guard.Finite(mutationRate, nameof(mutationRate));
        if (mutationRate is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(mutationRate));
        if (InputCount != other.InputCount || OutputCount != other.OutputCount) throw new ArgumentException("Parent dimensions must match.", nameof(other));
        // Equal-fitness crossover retains disjoint genes from both parents; matching genes are randomly chosen.
        var mergedNodes = nodes.Concat(other.nodes).GroupBy(n => n.Id).Select(g => g.First()).ToDictionary(n => n.Id);
        var genes = connections.Concat(other.connections).GroupBy(c => c.Innovation).OrderBy(g => g.Key).Select(g =>
        {
            var options = g.ToArray(); var gene = options[random.Next(options.Length)];
            bool enabled = options.Length == 1 ? gene.Enabled : options.All(c => c.Enabled) || random.NextDouble() >= .75;
            float weight = gene.Weight;
            if (random.NextDouble() < mutationRate) weight = Math.Clamp(weight + (random.NextSingle() - .5f) * .4f, -4, 4);
            return gene with { Weight = weight, Enabled = enabled };
        }).Where(c => mergedNodes[c.From].Depth < mergedNodes[c.To].Depth).ToList();
        // Keep graph growth bounded; structural mutations are intentionally rarer than weight changes.
        if (mergedNodes.Count <= InputCount + OutputCount + 128)
        {
            var child = new NeuralNetwork(new NetworkSnapshot(InputCount, OutputCount, mergedNodes.Values.ToArray(), genes.ToArray()));
            return child.MutateTopology(random, mutationRate * .15f, mutationRate * .25f);
        }
        return MutateTopology(random, mutationRate * .15f, mutationRate * .25f);
    }
    public NeuralNetwork MutateTopology(Random random, float addNodeProbability = .15f, float addConnectionProbability = .25f)
    {
        ArgumentNullException.ThrowIfNull(random);
        foreach (float p in new[] { addNodeProbability, addConnectionProbability }) if (!float.IsFinite(p) || p < 0 || p > 1) throw new ArgumentOutOfRangeException(nameof(addNodeProbability));
        var ns = nodes.ToList(); var cs = connections.ToList();
        if (HiddenNodeCount < 128 && random.NextDouble() < addNodeProbability)
        {
            var candidates = cs.Where(c => c.Enabled).ToArray();
            if (candidates.Length > 0)
            {
                var split = candidates[random.Next(candidates.Length)]; int id = SplitNodeId(split.Innovation);
                double depth = (nodes[slots[split.From]].Depth + nodes[slots[split.To]].Depth) / 2;
                // A rare hash collision is skipped, never silently merged with an unrelated node.
                if (!ns.Any(n => n.Id == id) && depth > nodes[slots[split.From]].Depth && depth < nodes[slots[split.To]].Depth)
                {
                    ns.Add(new(id, NeuralNodeKind.Hidden, -1, depth));
                    cs[cs.IndexOf(split)] = split with { Enabled = false };
                    cs.Add(new(Innovation(split.From, id), split.From, id, 1));
                    cs.Add(new(Innovation(id, split.To), id, split.To, split.Weight));
                }
            }
        }
        if (random.NextDouble() < addConnectionProbability)
        {
            for (int attempt = 0; attempt < 32; attempt++)
            {
                var from = ns[random.Next(ns.Count)]; var to = ns[random.Next(ns.Count)];
                if (from.Depth >= to.Depth || from.Kind == NeuralNodeKind.Output || to.Kind == NeuralNodeKind.Input) continue;
                int existing = cs.FindIndex(c => c.From == from.Id && c.To == to.Id);
                if (existing >= 0) { if (!cs[existing].Enabled) { cs[existing] = cs[existing] with { Enabled = true }; break; } continue; }
                cs.Add(new(Innovation(from.Id, to.Id), from.Id, to.Id, random.NextSingle() * 2 - 1)); break;
            }
        }
        return new(new NetworkSnapshot(InputCount, OutputCount, ns.ToArray(), cs.ToArray()));
    }
}

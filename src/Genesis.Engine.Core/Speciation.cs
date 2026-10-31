using System.Runtime.CompilerServices;

namespace Genesis.Engine.Core;

public readonly record struct CompatibilityMetrics(int Matching, int Disjoint, int Excess,
    double MeanWeightDifference, double Distance);

/// <summary>Innovation-aligned NEAT-style distance, including disabled genes. No phenotype or ancestry claim.</summary>
public static class GeneticCompatibility
{
    private static readonly ConditionalWeakTable<NeuralNetwork, ConnectionGene[]> Genes = new();
    private static ConnectionGene[] Sorted(NeuralNetwork brain) => Genes.GetValue(brain,
        b => b.ToSnapshot().Connections.OrderBy(c => c.Innovation).ToArray());

    public static double Distance(NeuralNetwork first, NeuralNetwork second) => Compare(first, second).Distance;

    public static CompatibilityMetrics Compare(NeuralNetwork first, NeuralNetwork second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.InputCount != second.InputCount || first.OutputCount != second.OutputCount)
            return new(0, 0, 0, 0, double.PositiveInfinity);
        var a = Sorted(first); var b = Sorted(second);
        int i = 0, j = 0, matching = 0, disjoint = 0, excess = 0;
        double difference = 0;
        long maxA = a.Length == 0 ? -1 : a[^1].Innovation;
        long maxB = b.Length == 0 ? -1 : b[^1].Innovation;
        while (i < a.Length || j < b.Length)
        {
            if (i < a.Length && j < b.Length && a[i].Innovation == b[j].Innovation)
            {
                difference += Math.Abs((double)a[i++].Weight - b[j++].Weight);
                matching++;
            }
            else if (i < a.Length && (j == b.Length || a[i].Innovation < b[j].Innovation))
            {
                if (a[i++].Innovation > maxB) excess++; else disjoint++;
            }
            else
            {
                if (b[j++].Innovation > maxA) excess++; else disjoint++;
            }
        }
        double mean = matching == 0 ? 0 : difference / matching;
        // Normalize all genome sizes, including small networks; coefficients are fixed and explicit.
        double distance = (disjoint + excess) / (double)Math.Max(1, Math.Max(a.Length, b.Length)) + .4 * mean;
        return new(matching, disjoint, excess, mean, distance);
    }
}

public sealed record SpeciesSummary(int Id, int MemberCount);

public sealed partial class Simulation
{
    public const double SpeciesCompatibilityThreshold = .65;
    private Agent[] classifiedAgents = [];
    private IReadOnlyList<SpeciesSummary> species = Array.Empty<SpeciesSummary>();
    /// <summary>Groups from the latest refresh/tick. IDs are representative agent IDs, not persistent lineage IDs.</summary>
    public IReadOnlyList<SpeciesSummary> Species => species;
    public int SpeciesCount => species.Count;
    /// <summary>Opt-in local cross-species fallback only when no eligible same-species mate exists.</summary>
    public bool AllowInterspeciesMating { get; set; }

    /// <summary>Refresh after directly changing Agents. Membership is deterministic in agent-ID order.
    /// Immutable brains are cached; unchanged membership avoids pairwise comparisons. At most one
    /// comparison per agent/representative pair (quadratic in population, linear in connection count).</summary>
    public void RefreshSpecies()
    {
        if (classifiedAgents.Length == Agents.Count && classifiedAgents.Where((a, i) => !ReferenceEquals(a, Agents[i])).Any() == false)
            return;
        var representatives = new List<Agent>();
        var counts = new Dictionary<int, int>();
        foreach (var agent in Agents.OrderBy(a => a.Id))
        {
            var representative = representatives.FirstOrDefault(r =>
                GeneticCompatibility.Distance(agent.Brain, r.Brain) <= SpeciesCompatibilityThreshold);
            if (representative is null)
            {
                representative = agent;
                representatives.Add(agent);
                counts.Add(agent.Id, 0);
            }
            agent.SpeciesId = representative.Id;
            counts[representative.Id]++;
        }
        species = Array.AsReadOnly(representatives.Select(r => new SpeciesSummary(r.Id, counts[r.Id])).ToArray());
        classifiedAgents = Agents.ToArray();
    }

    /// <summary>Bounded online reproductive score, not a measured task fitness or generational NEAT objective.
    /// Energy dominates; survival to maturity and prior offspring contribute bounded bonuses.</summary>
    public static double ReproductiveFitness(Agent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return Math.Clamp(agent.Energy, 0, 3) + .25 * Math.Clamp(agent.Age / 30, 0, 1)
            + .25 * Math.Clamp(agent.OffspringCount / 5.0, 0, 1);
    }
}

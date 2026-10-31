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
    private readonly List<SpeciesLineage> lineages = [];
    private long nextSpeciesId;
    private IReadOnlyList<SpeciesSummary> species = Array.Empty<SpeciesSummary>();
    // Only living lineages are retained: both memory and serialized snapshots are population-bounded.
    private sealed record SpeciesLineage(int Id, NeuralNetwork Representative);
    /// <summary>Living lineages from the latest refresh/tick, with stable IDs and member counts.</summary>
    public IReadOnlyList<SpeciesSummary> Species => species;
    public int SpeciesCount => species.Count;
    /// <summary>Opt-in local cross-species fallback only when no eligible same-species mate exists.</summary>
    public bool AllowInterspeciesMating { get; set; }

    /// <summary>Refresh after directly changing Agents. Classification uses frozen founding genomes
    /// in lineage-ID order, then agent-ID order; extinct lineages are discarded after each refresh.</summary>
    public void RefreshSpecies()
    {
        if (classifiedAgents.Length == Agents.Count && classifiedAgents.Where((a, i) => !ReferenceEquals(a, Agents[i])).Any() == false)
            return;
        // A directly replaced population must not inherit unrelated old representatives.
        // Existing members keep their own compatible lineage even if lineages overlap.
        var survivors = Agents.ToHashSet();
        var previousMembers = classifiedAgents.Where(survivors.Contains).ToDictionary(a => a, a => a.SpeciesId);
        if (classifiedAgents.Length != 0)
        {
            var survivingIds = previousMembers.Values.ToHashSet();
            lineages.RemoveAll(l => !survivingIds.Contains(l.Id));
        }
        var counts = new Dictionary<int, int>();
        foreach (var agent in Agents.OrderBy(a => a.Id))
        {
            SpeciesLineage? lineage = null;
            if (previousMembers.TryGetValue(agent, out int oldId))
                lineage = lineages.FirstOrDefault(l => l.Id == oldId &&
                    GeneticCompatibility.Distance(agent.Brain, l.Representative) <= SpeciesCompatibilityThreshold);
            lineage ??= lineages.FirstOrDefault(r =>
                GeneticCompatibility.Distance(agent.Brain, r.Representative) <= SpeciesCompatibilityThreshold);
            if (lineage is null)
            {
                // Preserve historical representative IDs for externally supplied agents and legacy saves.
                // The counter never decreases, so an extinct ID is never reused.
                long id = Math.Max(nextSpeciesId, (long)agent.Id);
                if (id > int.MaxValue) throw new InvalidOperationException("Species ID capacity reached.");
                lineage = new SpeciesLineage((int)id, new NeuralNetwork(agent.Brain.ToSnapshot()));
                nextSpeciesId = id + 1;
                lineages.Add(lineage);
            }
            agent.SpeciesId = lineage.Id;
            counts[lineage.Id] = counts.GetValueOrDefault(lineage.Id) + 1;
        }
        lineages.RemoveAll(l => !counts.ContainsKey(l.Id));
        species = Array.AsReadOnly(lineages.Select(l => new SpeciesSummary(l.Id, counts[l.Id])).ToArray());
        classifiedAgents = Agents.ToArray();
    }

    private SpeciesLineageSave[] SaveLineages() => lineages.Select(l =>
        new SpeciesLineageSave { Id = l.Id, Representative = l.Representative.ToSnapshot() }).ToArray();

    private void RestoreLineages(SpeciesLineageSave[]? saved, long next)
    {
        if (saved is null || saved.Length > MaximumPopulation || next < 0 || next > (long)int.MaxValue + 1)
            throw new ArgumentException("Invalid lineage snapshot.");
        lineages.Clear();
        var ids = new HashSet<int>();
        int previous = -1;
        foreach (var entry in saved)
        {
            if (entry is null || entry.Id < 0 || entry.Id <= previous || !ids.Add(entry.Id) ||
                entry.Id >= next || entry.Representative is null)
                throw new ArgumentException("Invalid lineage snapshot.");
            NeuralNetwork brain;
            try { brain = new NeuralNetwork(entry.Representative); }
            catch (ArgumentException ex) { throw new ArgumentException("Invalid lineage representative.", ex); }
            if (brain.InputCount != Agent.SensorCount || brain.OutputCount != 2)
                throw new ArgumentException("Invalid lineage representative dimensions.");
            lineages.Add(new SpeciesLineage(entry.Id, brain));
            previous = entry.Id;
        }
        nextSpeciesId = next;
        classifiedAgents = [];
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

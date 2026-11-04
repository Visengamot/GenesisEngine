namespace Genesis.Engine.Core;

/// <summary>Tick-local broad phase. Input rank, not bucket order, decides the mate.</summary>
internal sealed class MatingNeighborhood
{
    private Agent[] parents = [];
    private readonly int[] heads;
    private int[] next = [];
    private readonly int columns, rows;

    internal MatingNeighborhood(Agent[] rankedParents, int width, int height)
    {
        columns = (width + 2) / 3; rows = (height + 2) / 3;
        heads = new int[columns * rows];
        Rebuild(rankedParents);
    }

    internal void Rebuild(Agent[] rankedParents)
    {
        parents = rankedParents;
        Array.Fill(heads, -1);
        if (next.Length < parents.Length) Array.Resize(ref next, Math.Max(parents.Length, next.Length * 2));
        for (int rank = 0; rank < parents.Length; rank++)
        {
            var p = parents[rank].Position;
            int cell = (int)(p.Y / 3) * columns + (int)(p.X / 3);
            next[rank] = heads[cell]; heads[cell] = rank;
        }
    }

    internal Agent? Find(Agent parent, HashSet<int> used, bool allowInterspecies)
    {
        int bestSame = int.MaxValue, bestOther = int.MaxValue;
        int cx = (int)(parent.Position.X / 3), cy = (int)(parent.Position.Y / 3);
        for (int y = Math.Max(0, cy-1); y <= Math.Min(rows-1, cy+1); y++)
            for (int x = Math.Max(0, cx-1); x <= Math.Min(columns-1, cx+1); x++)
                for (int rank = heads[y*columns+x]; rank >= 0; rank = next[rank])
                {
                    var candidate = parents[rank];
                    bool same = candidate.SpeciesId == parent.SpeciesId;
                    if (same ? rank >= bestSame : !allowInterspecies || rank >= bestOther) continue;
                    if (candidate.Id == parent.Id || used.Contains(candidate.Id) ||
                        Math.Abs(candidate.Position.X-parent.Position.X) > 3 || Math.Abs(candidate.Position.Y-parent.Position.Y) > 3) continue;
                    if (same) bestSame = rank; else bestOther = rank;
                }
        int best = bestSame != int.MaxValue ? bestSame : bestOther;
        return best == int.MaxValue ? null : parents[best];
    }
}

namespace Genesis.Engine.Core;

internal static unsafe class NativeWorkNavigation
{
    internal const int MaxBudget = 512;

    private static readonly delegate* unmanaged[Cdecl]<nuint, nuint, float*, EnvironmentCell*, nuint, nuint, nuint, nuint, nuint, uint*, uint*, uint*, uint> search =
        (delegate* unmanaged[Cdecl]<nuint, nuint, float*, EnvironmentCell*, nuint, nuint, nuint, nuint, nuint, uint*, uint*, uint*, uint>)
        NativeEcology.FindExport("genesis_search_work_route");

    internal static bool IsAvailable => search != null;

    internal static PathSearchResult Search(TerrainMap terrain, EnvironmentGrid environment, Vec2 start, Vec2 end, int budget)
    {
        Guard.Position(start, nameof(start)); Guard.Position(end, nameof(end));
        if (budget is < 1 or > MaxBudget) throw new ArgumentOutOfRangeException(nameof(budget));
        int width = terrain.Width, height = terrain.Height;
        bool Inside(Vec2 p) => p.X >= 0 && p.Y >= 0 && p.X < width && p.Y < height;
        if (!Inside(start) || !Inside(end)) return new(PathSearchStatus.InvalidEndpoint, [], 0);
        uint* indices = stackalloc uint[budget];
        uint length, expanded;
        uint status;
        fixed (float* heights = terrain.Cells)
        fixed (EnvironmentCell* cells = environment.Cells)
            status = search((nuint)width, (nuint)height, heights, cells, (nuint)(int)start.X, (nuint)(int)start.Y,
                (nuint)(int)end.X, (nuint)(int)end.Y, (nuint)budget, indices, &length, &expanded);
        if (status != 0) return new((PathSearchStatus)status, [], (int)expanded);
        var path = new Vec2[length];
        for (int i = 0; i < path.Length; i++)
        {
            int index = (int)indices[i];
            path[i] = new(index % width, index / width);
        }
        return new(PathSearchStatus.Found, path, (int)expanded);
    }
}

public sealed partial class Simulation
{
    public static bool NativeWorkNavigationAvailable => NativeWorkNavigation.IsAvailable;

    /// <summary>Use the Rust worker pathfinder when present. Not part of world state.</summary>
    public bool UseNativeWorkNavigation { get; set; } = true;

    private PathSearchResult SearchWorkRoute(Vec2 start, Vec2 end, int budget)
        => UseNativeWorkNavigation && NativeWorkNavigation.IsAvailable && budget <= NativeWorkNavigation.MaxBudget
            ? NativeWorkNavigation.Search(Terrain, Environment, start, end, budget)
            : GridPathfinder.Search(Terrain, start, end, (x, y) => !HaulPassable(x, y), budget);
}

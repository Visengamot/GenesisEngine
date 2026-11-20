using System.Runtime.InteropServices;

namespace Genesis.Engine.Core;

internal static unsafe class NativeEcology
{
    // Load only a side-by-side library, not a same-named DLL from the search path.
    private static readonly delegate* unmanaged[Cdecl]<nuint, nuint, float, float*, float*, EnvironmentCell*, float*, void> step = Load();

    private static delegate* unmanaged[Cdecl]<nuint, nuint, float, float*, float*, EnvironmentCell*, float*, void> Load()
    {
        string name = OperatingSystem.IsWindows() ? "genesis_native.dll" :
            OperatingSystem.IsMacOS() ? "libgenesis_native.dylib" : "libgenesis_native.so";
        if (!NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, name), out var library)) return null;
        if (NativeLibrary.TryGetExport(library, "genesis_abi_version", out var version) &&
            ((delegate* unmanaged[Cdecl]<uint>)version)() == 1 &&
            NativeLibrary.TryGetExport(library, "genesis_step_ecology", out var entry))
            return (delegate* unmanaged[Cdecl]<nuint, nuint, float, float*, float*, EnvironmentCell*, float*, void>)entry;
        NativeLibrary.Free(library);
        return null;
    }

    internal static bool IsAvailable => step != null;

    internal static void Step(TerrainMap terrain, FoodMap food, EnvironmentGrid environment, float[] fire, float dt)
    {
        fixed (float* heights = terrain.Cells, amounts = food.Cells, snapshot = fire)
        fixed (EnvironmentCell* cells = environment.Cells)
            step((nuint)terrain.Width, (nuint)terrain.Height, dt, heights, amounts, cells, snapshot);
    }
}

public sealed partial class Simulation
{
    public static bool NativeEcologyAvailable => NativeEcology.IsAvailable;

    /// <summary>Use the side-by-side Rust implementation when present. Not part of world state.</summary>
    public bool UseNativeEcology { get; set; } = true;
}

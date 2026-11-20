using System.Runtime.InteropServices;

namespace Genesis.Engine.Core;

internal static unsafe class NativeEcology
{
    // Load only a side-by-side library, not a same-named DLL from the search path.
    private static readonly nint libraryHandle = Load();
    private static readonly delegate* unmanaged[Cdecl]<nuint, nuint, float, float*, float*, EnvironmentCell*, float*, void> step =
        (delegate* unmanaged[Cdecl]<nuint, nuint, float, float*, float*, EnvironmentCell*, float*, void>)FindExport("genesis_step_ecology");

    internal static nint FindExport(string name) =>
        libraryHandle != 0 && NativeLibrary.TryGetExport(libraryHandle, name, out var entry) ? entry : 0;

    private static nint Load()
    {
        string name = OperatingSystem.IsWindows() ? "genesis_native.dll" :
            OperatingSystem.IsMacOS() ? "libgenesis_native.dylib" : "libgenesis_native.so";
        if (!NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, name), out var library)) return 0;
        if (NativeLibrary.TryGetExport(library, "genesis_abi_version", out var version) &&
            ((delegate* unmanaged[Cdecl]<uint>)version)() == 1)
            return library;
        NativeLibrary.Free(library);
        return 0;
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

using System.Runtime.InteropServices;

namespace Genesis.Engine.Core;

[StructLayout(LayoutKind.Sequential)]
internal struct AgentMotionState(Vec2 position, Vec2 motion, Genome genome, float energy)
{
    internal float X = position.X, Y = position.Y, Vx = motion.X, Vy = motion.Y;
    internal float Speed = genome.Speed, Size = genome.Size, Metabolism = genome.Metabolism, Energy = energy;
}

internal static unsafe class NativeAgentMotion
{
    private static readonly delegate* unmanaged[Cdecl]<nuint, nuint, float*, float*, float, float, float, AgentMotionState*, void> step =
        (delegate* unmanaged[Cdecl]<nuint, nuint, float*, float*, float, float, float, AgentMotionState*, void>)
        NativeEcology.FindExport("genesis_move_and_feed");

    internal static bool IsAvailable => step != null;

    internal static AgentMotionState Step(TerrainMap terrain, FoodMap food, float dt, AgentMotionState state)
    {
        fixed (float* heights = terrain.Cells, amounts = food.Cells)
            step((nuint)terrain.Width, (nuint)terrain.Height, heights, amounts, dt, Simulation.DeepWaterLevel, Simulation.MountainLevel, &state);
        return state;
    }
}

public sealed partial class Simulation
{
    public static bool NativeAgentMotionAvailable => NativeAgentMotion.IsAvailable;

    /// <summary>Opt in to the experimental Rust movement and feeding kernel. Not part of world state.</summary>
    public bool UseNativeAgentMotion { get; set; }
}

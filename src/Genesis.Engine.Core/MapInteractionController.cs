using System.Numerics;

namespace Genesis.Engine.Core;

/// <summary>Distinguishes a map click from a left-button pan; Shift+brush is an explicit paint gesture.</summary>
public sealed class MapInteractionController
{
    public const float DragThresholdPixels = 6f;
    private Vector2 start;
    private Vector2 previous;
    private bool active;
    private bool dragging;
    private bool painting;

    public MapInteraction Update(Vector2 mouse, bool pressed, bool held, bool released, bool onMap, bool shiftBrush)
    {
        if (pressed)
        {
            active = onMap;
            start = previous = mouse;
            dragging = false;
            painting = active && shiftBrush;
        }

        if (!active) return default;

        Vector2 pan = Vector2.Zero;
        if (!painting && !dragging && (held || released) && Vector2.DistanceSquared(mouse, start) >= DragThresholdPixels * DragThresholdPixels)
        {
            dragging = true;
            pan = mouse - start;
        }
        else if (dragging && (held || released)) pan = mouse - previous;
        previous = mouse;

        bool paint = painting && held && onMap;
        bool click = released && !dragging && !painting && onMap;
        if (released || (!held && !pressed)) Reset();
        return new MapInteraction(pan, click, paint);
    }

    public void Reset()
    {
        active = dragging = painting = false;
    }
}

public readonly record struct MapInteraction(Vector2 PanDelta, bool Click, bool Paint);

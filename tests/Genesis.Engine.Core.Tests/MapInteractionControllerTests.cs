using System.Numerics;
using Genesis.Engine.Core;

namespace Genesis.Engine.Core.Tests;

public sealed class MapInteractionControllerTests
{
    [Fact]
    public void ClickIsOnlyReportedUponReleaseWithoutDragging()
    {
        var gesture = new MapInteractionController();
        Assert.Equal(default, gesture.Update(new(20, 30), true, true, false, true, false));
        Assert.Equal(default, gesture.Update(new(23, 33), false, true, false, true, false));
        var result = gesture.Update(new(23, 33), false, false, true, true, false);
        Assert.True(result.Click);
        Assert.Equal(Vector2.Zero, result.PanDelta);
        Assert.False(result.Paint);
    }

    [Fact]
    public void DragStartsAtSixPixelsAndNeverClicksOrPaints()
    {
        var gesture = new MapInteractionController();
        gesture.Update(new(20, 30), true, true, false, true, false);
        Assert.Equal(Vector2.Zero, gesture.Update(new(25, 30), false, true, false, true, false).PanDelta);
        var start = gesture.Update(new(26, 30), false, true, false, true, false);
        Assert.Equal(new Vector2(6, 0), start.PanDelta);
        Assert.False(start.Click || start.Paint);
        var move = gesture.Update(new(34, 34), false, true, false, true, false);
        Assert.Equal(new Vector2(8, 4), move.PanDelta);
        var release = gesture.Update(new(35, 34), false, false, true, true, false);
        Assert.Equal(new Vector2(1, 0), release.PanDelta);
        Assert.False(release.Click || release.Paint);
    }

    [Fact]
    public void FastReleaseAfterSixPixelMoveIsPanNotPlacement()
    {
        var gesture = new MapInteractionController();
        gesture.Update(new(10, 10), true, true, false, true, false);
        var release = gesture.Update(new(18, 10), false, false, true, true, false);
        Assert.Equal(new Vector2(8, 0), release.PanDelta);
        Assert.False(release.Click || release.Paint);
    }

    [Fact]
    public void ShiftBrushPaintsAndDoesNotPan()
    {
        var gesture = new MapInteractionController();
        var press = gesture.Update(new(10, 10), true, true, false, true, true);
        Assert.True(press.Paint);
        var move = gesture.Update(new(40, 10), false, true, false, true, true);
        Assert.True(move.Paint);
        Assert.Equal(Vector2.Zero, move.PanDelta);
        Assert.False(gesture.Update(new(40, 10), false, false, true, true, true).Click);
    }

    [Fact]
    public void GesturesBeginInViewportAndClickRequiresReleaseInside()
    {
        var gesture = new MapInteractionController();
        Assert.Equal(default, gesture.Update(new(10, 10), true, true, false, false, false));
        Assert.Equal(default, gesture.Update(new(30, 10), false, false, true, true, false));
        gesture.Update(new(10, 10), true, true, false, true, false);
        Assert.False(gesture.Update(new(10, 10), false, false, true, false, false).Click);
    }

    [Fact]
    public void PanMayContinueOutsideViewportButNextGestureIsIndependent()
    {
        var gesture = new MapInteractionController();
        gesture.Update(new(10, 10), true, true, false, true, false);
        var move = gesture.Update(new(20, 10), false, true, false, false, false);
        Assert.Equal(new Vector2(10, 0), move.PanDelta);
        gesture.Update(new(20, 10), false, false, true, false, false);
        gesture.Update(new(20, 10), true, true, false, true, false);
        Assert.True(gesture.Update(new(20, 10), false, false, true, true, false).Click);
    }
}

using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// What a wheel turn over a scrollbar does: the bar owns its own axis, so the turn must not reach
/// the panel and come back out on the other axis.
/// </summary>
/// <remarks>
/// A panel answers a turn it cannot use vertically by scrolling horizontally, which is documented
/// and wanted while the pointer is over the content. It is the wrong answer when the pointer is over
/// a bar: the user aimed at one axis, and a bar at its end used to leave the turn for its owner,
/// which then scrolled the other axis. These tests drive the real wheel path
/// (<see cref="WindowBackend.SimulateMouseWheel(SKPoint, float)"/>) rather than the scrolling
/// API, because the API never had the bug.
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class ScrollBarWheelTests : HeadlessTestBase
{
    [TestMethod]
    public void AHorizontalBarAtItsStartKeepsATurnTowardTheStartOffTheVerticalAxis()
    {
        var panel = BothWays();
        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        // With room on both axes the turn moves the axis the pointer is on, and only that one.
        panel.ScrollTo(100f, 100f);
        Assert.IsTrue(scene.WindowBackend.SimulateMouseWheel(HorizontalBarPoint(panel), 48f), "the bar consumes the turn");
        Assert.AreEqual(52f, panel.HorizontalOffset, 0.5f, "the turn moved the horizontal axis by one detent");
        Assert.AreEqual(100f, panel.VerticalOffset, 0.5f, "and left the vertical axis alone");

        // At the start of the horizontal range the same turn can move nothing. It is still the
        // horizontal bar's turn: the panel must not answer it by scrolling vertically.
        panel.ScrollTo(0f, 100f);
        Assert.IsTrue(
            scene.WindowBackend.SimulateMouseWheel(HorizontalBarPoint(panel), 48f),
            "a bar at its end still consumes the turn rather than handing it back");
        Assert.AreEqual(0f, panel.HorizontalOffset, 0.5f, "there is nothing before the start");
        Assert.AreEqual(100f, panel.VerticalOffset, 0.5f, "and the turn must not turn into a vertical scroll");
    }

    [TestMethod]
    public void AHorizontalBarAtItsEndDoesNotHandTheTurnToThePanelAroundIt()
    {
        var inner = Plain();
        inner.Bounds = new SKRect(0f, 0f, 200f, 100f);
        inner.HorizontalScroll = ScrollStyle.Automatic;
        var wide = Plain();
        wide.Bounds = new SKRect(0f, 0f, 800f, 60f);
        inner.Controls.Add(wide);

        var outer = Plain();
        outer.VerticalScroll = ScrollStyle.Automatic;
        var tall = Plain();
        tall.Bounds = new SKRect(0f, 120f, 200f, 800f);
        outer.Controls.Add(inner);
        outer.Controls.Add(tall);

        using var scene = HeadlessScene.Create(outer, 240f, 300f);

        inner.ScrollTo(inner.HorizontalOffset + 10_000f, 0f);
        Assert.IsTrue(inner.HorizontalOffset > 0f, "the inner panel is at the end of its horizontal range");
        Assert.IsTrue(outer.CanScrollVertically, "and the panel around it could scroll vertically");

        var before = inner.HorizontalOffset;
        var outerBefore = outer.VerticalOffset;

        Assert.IsTrue(
            scene.WindowBackend.SimulateMouseWheel(HorizontalBarPoint(inner), -48f),
            "the bar consumes the turn even at its end");

        Assert.AreEqual(before, inner.HorizontalOffset, 0.5f, "the inner panel is still at its end");
        Assert.AreEqual(
            outerBefore,
            outer.VerticalOffset,
            0.5f,
            "and the panel around it does not scroll: the turn went to the bar, not to an ancestor");
    }

    [TestMethod]
    public void AVerticalBarAtItsEndDoesNotScrollTheHorizontalAxis()
    {
        var panel = BothWays();
        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        panel.ScrollTo(0f, panel.VerticalOffset + 10_000f);
        Assert.IsTrue(panel.VerticalOffset > 0f, "the panel is at the end of its vertical range");

        Assert.IsTrue(
            scene.WindowBackend.SimulateMouseWheel(VerticalBarPoint(panel), -48f),
            "the vertical bar consumes the turn at its end");
        Assert.AreEqual(0f, panel.HorizontalOffset, 0.5f, "with no horizontal movement to fall back to");
    }

    [TestMethod]
    public void ATurnOverTheContentAtTheVerticalEndIsStillDeclinedRatherThanTurnedSideways()
    {
        var panel = BothWays();
        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        panel.ScrollTo(0f, panel.VerticalOffset + 10_000f);
        Assert.IsTrue(panel.VerticalOffset > 0f, "the vertical axis is at its end");

        // The behaviour over the content, guarded so this fix cannot creep into it: the panel tries
        // the axis the wheel asks for, cannot move it, and declines the turn. It does not answer by
        // scrolling the other axis, and over the content that is deliberate.
        Assert.IsFalse(
            scene.WindowBackend.SimulateMouseWheel(new SKPoint(50f, 40f), -48f),
            "a panel that cannot move the axis it was asked about leaves the turn alone");
        Assert.AreEqual(0f, panel.HorizontalOffset, 0.5f, "and the other axis is not moved on its behalf");
    }

    [TestMethod]
    public void ATurnOverTheContentWithNoVerticalRoomFallsThroughToTheHorizontalAxis()
    {
        var panel = Plain();
        panel.HorizontalScroll = ScrollStyle.Automatic;
        var content = Plain();
        content.Bounds = new SKRect(0f, 0f, 600f, 40f);
        panel.Controls.Add(content);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        Assert.IsFalse(panel.CanScrollVertically, "there is nothing to scroll vertically");

        // The other half of the documented fallback: no vertical range at all means the horizontal
        // axis answers the turn.
        Assert.IsTrue(
            scene.WindowBackend.SimulateMouseWheel(new SKPoint(50f, 20f), -48f),
            "the panel consumes the turn");
        Assert.AreEqual(48f, panel.HorizontalOffset, 0.5f, "the horizontal axis took the turn");
    }

    /// <summary>
    /// A panel with content that overflows both ways, so both bars exist and both axes can move.
    /// </summary>
    private static Panel BothWays()
    {
        var panel = Plain();
        panel.HorizontalScroll = ScrollStyle.Automatic;
        panel.VerticalScroll = ScrollStyle.Automatic;

        var content = Plain();
        content.Bounds = new SKRect(0f, 0f, 600f, 600f);
        panel.Controls.Add(content);
        return panel;
    }

    /// <summary>The middle of the horizontal bar's track, in host coordinates.</summary>
    private static SKPoint HorizontalBarPoint(Panel panel)
    {
        var rect = panel.HorizontalScrollBar!.Bounds;
        return new SKPoint(rect.MidX, rect.MidY);
    }

    /// <summary>The middle of the vertical bar's track, in host coordinates.</summary>
    private static SKPoint VerticalBarPoint(Panel panel)
    {
        var rect = panel.VerticalScrollBar!.Bounds;
        return new SKPoint(rect.MidX, rect.MidY);
    }

    private static Panel Plain() => new()
    {
        BorderWidth = 0f,
        CornerRadius = 0f,
        Padding = Thickness.Empty,
    };
}

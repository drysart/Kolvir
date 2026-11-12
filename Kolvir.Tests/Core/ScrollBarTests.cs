using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The standalone scroll bar: arrows at both ends, a track that pages, a draggable thumb, and the
/// two events that tell a container its own layout from the user's scrolling.
/// </summary>
/// <remarks>
/// The bar is a value slider with published geometry (<see cref="ScrollBar.ThumbRect"/>,
/// <see cref="ScrollBar.TrackRect"/>), so a click can be aimed at a real part of it instead of
/// at a magic pixel.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class ScrollBarTests : HeadlessTestBase
{
    [TestMethod]
    public void TheArrowButtonsScrollBySmallChange()
    {
        var bar = HorizontalBar(value: 50f);

        using var scene = HeadlessScene.Create(bar, 220f, 20f);

        scene.ClickAt(PointIn(bar, bar.Width - 6f, bar.Height / 2f));
        Assert.AreEqual(51f, bar.Value, "the end arrow should step by SmallChange");

        scene.ClickAt(PointIn(bar, 6f, bar.Height / 2f));
        Assert.AreEqual(50f, bar.Value, "the start arrow should step back");
    }

    [TestMethod]
    public void ClickingTheTrackPagesByLargeChange()
    {
        var bar = HorizontalBar(value: 50f);

        using var scene = HeadlessScene.Create(bar, 220f, 20f);

        // To the right of the thumb, but inside the track.
        scene.ClickAt(new SKPoint(bar.TrackRect.Right - 4f, bar.Height / 2f));
        Assert.AreEqual(60f, bar.Value, "a track click pages forward by LargeChange");

        scene.ClickAt(new SKPoint(bar.TrackRect.Left + 4f, bar.Height / 2f));
        Assert.AreEqual(50f, bar.Value, "and a click at the other end pages back");
    }

    [TestMethod]
    public void DraggingTheThumbFollowsThePointer()
    {
        var bar = HorizontalBar(value: 50f);

        using var scene = HeadlessScene.Create(bar, 220f, 20f);

        var origin = bar.GetPositionInHost();
        var start = new SKPoint(origin.X + bar.ThumbRect.MidX, origin.Y + bar.ThumbRect.MidY);

        scene.Host.ProcessMouseMove(start);
        scene.Host.ProcessMouseDown(start);
        scene.Host.ProcessMouseMove(new SKPoint(start.X + 40f, start.Y));
        scene.Host.ProcessMouseUp(new SKPoint(start.X + 40f, start.Y));

        // 40 px along a track that spans the range is a little over a fifth of it, so the value
        // moves from the middle towards the end rather than jumping to it.
        Assert.IsTrue(bar.Value > 60f && bar.Value < 90f, $"a 40 px drag should move the value part way, got {bar.Value}");
    }

    [TestMethod]
    public void ADragStopsAtTheMaximum()
    {
        var bar = HorizontalBar(value: 50f);

        using var scene = HeadlessScene.Create(bar, 220f, 20f);

        var origin = bar.GetPositionInHost();
        var start = new SKPoint(origin.X + bar.ThumbRect.MidX, origin.Y + bar.ThumbRect.MidY);

        scene.Host.ProcessMouseMove(start);
        scene.Host.ProcessMouseDown(start);
        scene.Host.ProcessMouseMove(new SKPoint(start.X + 10_000f, start.Y));
        scene.Host.ProcessMouseUp(new SKPoint(start.X + 10_000f, start.Y));

        Assert.AreEqual(100f, bar.Value, "a drag past the end clamps to MaxValue");
    }

    [TestMethod]
    public void OnlyUserScrollingRaisesScrollAndValueChangedRaisesForBoth()
    {
        var bar = HorizontalBar(value: 10f);

        using var scene = HeadlessScene.Create(bar, 220f, 20f);

        int scrolled = 0, changed = 0;
        bar.AddScrollHandler(_ => scrolled++);
        bar.AddValueChangedHandler(_ => changed++);

        bar.Value = 20f;

        Assert.AreEqual(0, scrolled, "an assignment is not the user scrolling");
        Assert.AreEqual(1, changed, "but it is a value change");

        scene.ClickAt(PointIn(bar, bar.Width - 6f, bar.Height / 2f));

        Assert.AreEqual(1, scrolled, "an arrow press is user scrolling");
        Assert.AreEqual(2, changed);
    }

    [TestMethod]
    public void TheThumbIsSizedFromLargeChange()
    {
        var narrow = HorizontalBar(value: 0f);
        narrow.LargeChange = 10f;

        var wide = HorizontalBar(value: 0f);
        wide.LargeChange = 90f;

        // One application at a time: each scene tears its own down when it is disposed.
        float narrowThumb;
        using (var scene = HeadlessScene.Create(narrow, 220f, 20f))
        {
            narrowThumb = narrow.ThumbRect.Width;
        }

        float wideThumb;
        using (var scene = HeadlessScene.Create(wide, 220f, 20f))
        {
            wideThumb = wide.ThumbRect.Width;
        }

        Assert.IsTrue(
            wideThumb > narrowThumb,
            $"a bigger page should mean a longer thumb ({narrowThumb} vs {wideThumb})");
    }

    [TestMethod]
    public void ADisabledBarTakesNoInput()
    {
        var bar = HorizontalBar(value: 50f);

        using var scene = HeadlessScene.Create(bar, 220f, 20f);

        bar.Enabled = false;
        scene.ClickAt(PointIn(bar, bar.Width - 6f, bar.Height / 2f));

        Assert.AreEqual(50f, bar.Value, "a disabled bar should not scroll");
        Assert.IsFalse(bar.IsScrollable);
    }

    /// <summary>A horizontal bar with the range and steps every test in this class assumes.</summary>
    private static ScrollBar HorizontalBar(float value) => new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Horizontal,
        Minimum = 0f,
        Maximum = 100f,
        Value = value,
        SmallChange = 1f,
        LargeChange = 10f,
    };

    /// <summary>A point in the bar, given in the bar's own coordinates.</summary>
    private static SKPoint PointIn(Control bar, float x, float y)
    {
        var origin = bar.GetPositionInHost();
        return new SKPoint(origin.X + x, origin.Y + y);
    }
}

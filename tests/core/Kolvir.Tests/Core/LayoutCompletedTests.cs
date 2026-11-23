using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// The layout-completed notification: the seam a feature uses to recompute after a pass without
/// registering anything with a window.
/// </summary>
/// <remarks>
/// The cases that matter are the quiet ones - a paint that arranged nothing, a scroll that did not
/// move - because those are the frames a notification that over-fires would turn into cost. The
/// recursion case pins the rule that a handler may lay out again but cannot report itself.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class LayoutCompletedTests : HeadlessTestBase
{
    [TestMethod]
    public void AWholeTreePassRaisesTheEventOnce()
    {
        using var scene = HeadlessScene.Create(new Panel());

        int count = 0;

        using var subscription = UiHost.AddLayoutCompletedHandler(_ => count++);

        scene.Host.PerformLayout();

        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public void APaintWithNothingToArrangeDoesNotRaiseTheEvent()
    {
        using var scene = HeadlessScene.Create(new Panel());

        int count = 0;

        using var subscription = UiHost.AddLayoutCompletedHandler(_ => count++);

        scene.Paint();

        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public void AQueuedArrangementRaisesTheEventWhenItRuns()
    {
        var root = new Panel();
        var child = new Panel();

        root.Controls.Add(child);

        using var scene = HeadlessScene.Create(root);

        int count = 0;

        using var subscription = UiHost.AddLayoutCompletedHandler(_ => count++);

        scene.Host.RequestLayout(child);
        scene.Paint();

        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public void AScrollThatMovesRaisesTheEventAndOneThatDoesNotDoesNot()
    {
        var panel = new Panel { VerticalScroll = ScrollStyle.Always };
        panel.Controls.Add(new Panel { Bounds = new SKRect(0f, 0f, 40f, 400f) });

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        int count = 0;

        using var subscription = UiHost.AddLayoutCompletedHandler(_ => count++);

        panel.ScrollTo(0f, 0f);

        Assert.AreEqual(0, count, "nothing moved, so nothing settled");

        panel.ScrollTo(0f, 50f);

        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public void AHandlerThatLaysOutAgainDoesNotRaiseTheEventItself()
    {
        using var scene = HeadlessScene.Create(new Panel());

        int count = 0;

        using var subscription = UiHost.AddLayoutCompletedHandler(_ =>
        {
            count++;
            scene.Host.PerformLayout();
        });

        scene.Host.PerformLayout();

        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public void RemovingTheHandlerStopsIt()
    {
        using var scene = HeadlessScene.Create(new Panel());

        int count = 0;

        Action<UiHost> handler = _ => count++;

        UiHost.AddLayoutCompletedHandler(handler);
        scene.Host.PerformLayout();

        UiHost.RemoveLayoutCompletedHandler(handler);
        scene.Host.PerformLayout();

        Assert.AreEqual(1, count);
    }
}

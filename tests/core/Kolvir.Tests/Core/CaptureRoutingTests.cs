using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// What happens to the pointer capture when the platform takes the gesture away from the UI: the
/// capture has to end with the press, so the wheel and the next press hit-test again.
/// </summary>
/// <remarks>
/// <para>
/// A press captures the control it lands on, and a release ends it. Some gestures never deliver a
/// release: on Win32, a window moved by its own drag region hands the press to the system's modal
/// move loop, which consumes the button-up. A capture that outlives its press is not cosmetic -
/// <see cref="UiHost.ProcessMouseWheel"/> resolves its target as the captured control first - so the
/// wheel goes to the control the drag started on, wherever the pointer really is, until something
/// else presses and releases inside the UI. That is the whole reported defect, and
/// <see cref="UiHost.ProcessCaptureLost"/> is the notice the platform sends instead of the release.
/// </para>
/// <para>
/// The tree is the shape of the report: a drag region in one branch of the tree and a scrolling panel
/// in another, so a wheel routed to the stale capture reaches nothing that scrolls.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class CaptureRoutingTests : HeadlessTestBase
{
    private static readonly SKPoint WheelPoint = new(200f, 200f);

    [TestMethod]
    public void ACaptureThatThePlatformTookAwayLeavesTheWheelToTheControlUnderThePointer()
    {
        var (root, dragRegion, scroller) = Build();
        using var scene = HeadlessScene.Create(root, 240f, 300f);

        var dragRegionWheels = 0;
        dragRegion.AddMouseWheelHandler((_, _) => dragRegionWheels++);

        scene.Press(dragRegion);
        Assert.AreSame(dragRegion, scene.Host.CapturedControl, "the press captured the drag region");

        // While the capture is live the wheel belongs to it, which is what a drag inside the UI needs.
        Assert.IsFalse(scene.WindowBackend.SimulateMouseWheel(WheelPoint, -48f), "the captured control cannot scroll anything");
        Assert.AreEqual(1, dragRegionWheels, "so the turn went to the capture, not to the panel under the pointer");
        Assert.AreEqual(0f, scroller.VerticalOffset, 0.5f, "and the panel did not move");

        // The platform took the pointer - the system's move loop, or a capture another window took -
        // so no release is coming and the UI has to stop routing to the control the press began on.
        scene.Host.ProcessCaptureLost();

        Assert.IsNull(scene.Host.CapturedControl, "the capture ended with the press it belonged to");

        Assert.IsTrue(scene.WindowBackend.SimulateMouseWheel(WheelPoint, -48f), "the panel under the pointer takes the turn");
        Assert.AreEqual(48f, scroller.VerticalOffset, 0.5f, "which is the scroll the user asked for");
        Assert.AreEqual(1, dragRegionWheels, "the drag region is not sent wheel turns for a pointer that is not over it");
    }

    [TestMethod]
    public void ACaptureLostWithoutAReleaseRaisesNoReleaseOnTheControl()
    {
        var (root, dragRegion, _) = Build();
        using var scene = HeadlessScene.Create(root, 240f, 300f);

        var releases = 0;
        dragRegion.AddMouseUpHandler((_, _) => releases++);

        scene.Press(dragRegion);
        scene.Host.ProcessCaptureLost();

        Assert.AreEqual(0, releases, "the platform owns the gesture now, so the control is not told its drag ended");
        Assert.IsNull(scene.Host.CapturedControl, "but the capture is gone");
    }

    [TestMethod]
    public void ACaptureInsideTheUiStillReceivesThePointerOutsideTheControl()
    {
        var (root, dragRegion, scroller) = Build();
        using var scene = HeadlessScene.Create(root, 240f, 300f);

        var dragRegionMoves = 0;
        var scrollerMoves = 0;
        dragRegion.AddMouseMoveHandler((_, _) => dragRegionMoves++);
        scroller.AddMouseMoveHandler((_, _) => scrollerMoves++);

        scene.Press(dragRegion);
        scene.MoveTo(WheelPoint);

        Assert.AreEqual(1, dragRegionMoves, "a capture in progress keeps the moves, wherever the pointer goes");
        Assert.AreEqual(0, scrollerMoves, "so the control under the pointer does not see them");

        scene.Host.ProcessMouseUp(WheelPoint, MouseButtons.Left);
        Assert.IsNull(scene.Host.CapturedControl, "and the release ends it");

        scene.MoveTo(WheelPoint);
        Assert.AreEqual(1, scrollerMoves, "after which the pointer hit-tests again");
    }

    /// <summary>
    /// A drag region and a scrolling panel as siblings, so a wheel routed to the drag region cannot
    /// reach the scrolling one through a parent.
    /// </summary>
    private static (Panel Root, Panel DragRegion, Panel Scroller) Build()
    {
        var dragRegion = Plain();
        dragRegion.Bounds = new SKRect(0f, 0f, 240f, 40f);

        var content = Plain();
        content.Bounds = new SKRect(0f, 0f, 150f, 600f);

        var scroller = Plain();
        scroller.Bounds = new SKRect(0f, 40f, 240f, 300f);
        scroller.VerticalScroll = ScrollStyle.Automatic;
        scroller.Controls.Add(content);

        var root = Plain();
        root.Controls.Add(scroller);
        root.Controls.Add(dragRegion);
        return (root, dragRegion, scroller);
    }

    private static Panel Plain() => new()
    {
        Padding = Thickness.Empty,
    };
}

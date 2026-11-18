using Kolvir;
using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// The frame-starting hook: the one point in a frame where the tree can still change and still be
/// drawn by the frame that is starting.
/// </summary>
/// <remarks>
/// The hook exists for work that has been deferred to "the next frame", so the test that matters is the
/// second one: a control that arrives from the hook is laid out and measured by the frame that is
/// already underway, not by the one after it. Every handler here is removed, because the list is static
/// and one left behind would count frames in every other test in the assembly.
/// </remarks>
[TestClass]
public sealed class FrameStartingTests
{
    [TestMethod]
    public void AFrameStartingHandlerRunsOncePerFrame()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(panel, 100f, 80f);

        var hosts = new List<UiHost?>();
        using var token = UiHost.AddFrameStartingHandler(host => hosts.Add(host));

        scene.Paint();
        scene.Paint();

        Assert.AreEqual(2, hosts.Count, "one call per frame drawn");
        Assert.AreSame(scene.Host, hosts[0], "and the handler is told which host is drawing");
    }

    [TestMethod]
    public void AControlAddedByAFrameStartingHandlerIsLaidOutInThatFrame()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(panel, 100f, 80f);

        var late = new Label("late") { Dock = DockStyle.Fill };
        bool added = false;

        using var token = UiHost.AddFrameStartingHandler(_ =>
        {
            if (added)
            {
                return;
            }

            added = true;
            panel.Controls.Add(late);
        });

        scene.Paint();

        Assert.IsTrue(late.Width > 0f && late.Height > 0f, "the frame that is starting laid the new control out");
        Assert.AreEqual(panel.ContentRect.Width, late.Width, 0.01f, "and arranged it to the panel's content area");
        Assert.AreEqual(panel.ContentRect.Height, late.Height, 0.01f);
    }

    [TestMethod]
    public void DisposingTheTokenStopsTheHandler()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(panel, 100f, 80f);
        int calls = 0;

        var token = UiHost.AddFrameStartingHandler(_ => calls++);
        scene.Paint();
        token.Dispose();
        scene.Paint();

        Assert.AreEqual(1, calls, "the frame after the token was disposed did not run the handler");
    }

    [TestMethod]
    public void AHandlerThatInvalidatesIsPaintedByTheNextFrame()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(panel, 100f, 80f);

        int frames = 0;
        using var token = UiHost.AddFrameStartingHandler(_ =>
        {
            frames++;

            // Asking for a frame from inside one is not a mistake: it lands in the empty region the
            // paint is about to take, so the work it asks for belongs to the frame after this one.
            scene.Host.Invalidate();
        });

        scene.Paint();
        scene.Paint();

        Assert.AreEqual(2, frames, "two paints are two frames, whatever a handler asked for");
    }
}

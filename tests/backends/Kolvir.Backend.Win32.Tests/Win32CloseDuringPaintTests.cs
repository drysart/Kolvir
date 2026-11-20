using Kolvir;
using SkiaSharp;

namespace Kolvir.Backend.Win32.Tests;

/// <summary>
/// The native version of the ordering rule: a close asked for by a frame-starting handler must not
/// dispose the surface the Win32 paint path is still holding.
/// </summary>
/// <remarks>
/// This is the path that produced <see cref="System.ExecutionEngineException"/>: the frame-start
/// drain runs inside <c>WM_PAINT</c>, so a synchronous <see cref="Window.Close"/> freed the Skia
/// surface before <c>canvas.Restore</c>. A managed-only test cannot prove the native call survives,
/// which is why this one runs a real Win32 window and its real message loop.
/// </remarks>
[TestClass]
public sealed class Win32CloseDuringPaintTests
{
    [TestMethod]
    [Timeout(30000)]
    public void ClosingFromAFrameStartingHandlerDoesNotTearDownInsideThePaint()
    {
        var application = new Application(new Win32ApplicationBackend());

        try
        {
            var window = new Window("close during paint", new Panel { Dock = DockStyle.Fill })
            {
                ClientSize = new SKSize(160f, 120f),
            };

            bool requested = false;
            int closed = 0;
            window.AddClosedHandler(_ => closed++);

            using var token = UiHost.AddFrameStartingHandler(host =>
            {
                if (requested || !ReferenceEquals(host, window.Host))
                {
                    return;
                }

                requested = true;
                window.Close();

                Assert.IsTrue(window.IsClosed, "the model window is closed at once");
                Assert.AreEqual(0, closed, "the teardown waits for a loop turn outside WM_PAINT");
            });

            window.Show();

            // A frame is guaranteed even if showing the window did not already invalidate it: the
            // queued turn runs before the loop parks.
            application.BeginInvoke(() => window.Host.Invalidate());

            application.Run();

            Assert.IsTrue(requested, "the real Win32 paint path ran the frame-starting handler");
            Assert.AreEqual(1, closed, "the deferred turn closed the window after the paint");
            Assert.IsTrue(window.IsClosed);
        }
        finally
        {
            application.Dispose();
        }
    }
}

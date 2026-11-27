using Kolvir;
using SkiaSharp;

namespace Kolvir.Backend.Win32.Tests;

/// <summary>
/// The window procedure's half of the pointer-capture bookkeeping: a capture the platform takes away
/// has to end the press, so the next wheel message is routed by hit-test rather than to the control
/// the press began on.
/// </summary>
/// <remarks>
/// <para>
/// The defect this pins was reported as "the wheel stops working until I click something", and it is
/// reproduced by moving a window by its own drag region: the press captures the drag region, the
/// system's modal move loop consumes the button-up, and every later wheel turn is routed to the drag
/// region instead of to the control under the pointer. The move loop itself cannot be driven here -
/// <c>WM_NCLBUTTONDOWN</c> does not return until the user releases the button - so the test sends the
/// two things that loop leaves behind: a press, and the <c>WM_CAPTURECHANGED</c> the window receives
/// when the pointer is no longer its own. <see cref="UiHost.ProcessCaptureLost"/> is the notice the
/// handoff raises before the loop starts.
/// </para>
/// <para>
/// Like the other tests in this assembly that need a window, this one is disabled for the automated
/// suite: it creates a real Win32 window, which the headless runners have no desktop for.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32WheelRoutingTests
{
    [TestMethod]
    [Timeout(30000)]
    [Ignore("Opens a real Win32 window; disabled so the automated suite does not create windows.")]
    public void ACaptureChangeNoticeEndsThePressSoTheWheelHitTestsAgain()
    {
        var application = new Application(new Win32ApplicationBackend());

        try
        {
            var dragRegion = new Panel { Bounds = new SKRect(0f, 0f, 300f, 40f) };
            var target = new Panel { Bounds = new SKRect(0f, 40f, 300f, 220f) };

            var dragRegionWheels = 0;
            var targetWheels = 0;
            dragRegion.AddMouseWheelHandler((_, _) => dragRegionWheels++);
            target.AddMouseWheelHandler((_, e) =>
            {
                targetWheels++;
                e.Handled = true;
            });

            var root = new Panel { Dock = DockStyle.Fill };
            root.Controls.Add(target);
            root.Controls.Add(dragRegion);

            var window = new Window("wheel routing", root) { ClientSize = new SKSize(300f, 220f) };

            window.AddShownHandler(_ =>
            {
                try
                {
                    var hwnd = ((Win32WindowBackend)window.Backend).Handle;

                    // A press in the client area, which is what captures the pointer.
                    Send(hwnd, NativeMethods.WmLButtonDown, 0, Pack(150, 20));
                    Assert.AreSame(
                        dragRegion,
                        window.Host.CapturedControl,
                        "the press captured the control it landed on");

                    // Still captured: the turn belongs to the drag region, wherever the pointer is.
                    Send(hwnd, NativeMethods.WmMouseWheel, Wheel(120), ScreenPack(hwnd, 150, 150));
                    Assert.AreEqual(1, dragRegionWheels, "a live capture keeps the wheel");
                    Assert.AreEqual(0, targetWheels, "so the control under the pointer does not see it");

                    // The platform took the pointer: the press is over and the window says so.
                    Send(hwnd, NativeMethods.WmCaptureChanged, 0, 0);
                    Assert.IsNull(window.Host.CapturedControl, "the press the capture belonged to is over");

                    Send(hwnd, NativeMethods.WmMouseWheel, Wheel(-120), ScreenPack(hwnd, 150, 150));
                    Assert.AreEqual(1, targetWheels, "the wheel is routed by hit-test again");
                    Assert.AreEqual(1, dragRegionWheels, "and the drag region is left out of it");
                }
                finally
                {
                    application.Exit();
                }
            });

            window.Show();
            application.Run();
        }
        finally
        {
            application.Dispose();
        }
    }

    private static IntPtr Send(IntPtr hwnd, uint message, int wParam, int lParam) =>
        NativeMethods.SendMessageW(hwnd, message, new IntPtr(wParam), new IntPtr(lParam));

    /// <summary>A client-area point as <c>lParam</c>, which is what the button messages carry.</summary>
    private static int Pack(int x, int y) => (y << 16) | (x & 0xFFFF);

    /// <summary>
    /// A client-area point as the screen point a wheel message carries, which is the one mouse
    /// message that is not in client coordinates.
    /// </summary>
    private static int ScreenPack(IntPtr hwnd, int x, int y)
    {
        var point = new NativeMethods.POINT { X = x, Y = y };
        NativeMethods.ClientToScreen(hwnd, ref point);
        return Pack(point.X, point.Y);
    }

    /// <summary>The high word of a wheel message's <c>wParam</c>, in 120ths of a detent.</summary>
    private static int Wheel(int delta) => delta << 16;
}

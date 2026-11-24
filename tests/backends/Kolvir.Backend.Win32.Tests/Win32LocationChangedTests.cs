using System.Runtime.InteropServices;
using Kolvir;
using SkiaSharp;

namespace Kolvir.Backend.Win32.Tests;

/// <summary>
/// The native half of move reporting: a real Win32 window moved with <c>SetWindowPos</c> must raise
/// the same handler the model observes.
/// </summary>
/// <remarks>
/// The core test drives the headless backend, which proves the model wiring but not that the Win32
/// message path reaches it. This one runs the real message loop and moves the real window.
/// </remarks>
[TestClass]
public sealed class Win32LocationChangedTests
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [TestMethod]
    [Timeout(30000)]
    [Ignore("Creates a Win32ApplicationBackend and opens a real Win32 window; disabled so the automated suite does not create windows.")]
    public void MovingARealWindowRaisesLocationChanged()
    {
        var application = new Application(new Win32ApplicationBackend());

        try
        {
            var window = new Window("move", new Panel())
            {
                ClientSize = new SKSize(320f, 200f),
            };

            var moves = new List<SKPoint>();
            window.AddLocationChangedHandler(_ => moves.Add(window.Location));

            window.AddShownHandler(_ =>
            {
                var backend = (Win32WindowBackend)window.Backend;

                Assert.AreEqual(0, moves.Count, "showing and placing the window is not a user move");

                Assert.IsTrue(
                    SetWindowPos(backend.Handle, IntPtr.Zero, 300, 220, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate),
                    "SetWindowPos should move the real window");

                Assert.AreEqual(1, moves.Count, "moving the window raises the handler once");
                Assert.AreEqual(window.Location, moves[0], "with the position it moved to");

                application.Exit();
            });

            window.Show();
            application.Run();

            Assert.AreEqual(1, moves.Count, "the move was reported once for the whole run");
        }
        finally
        {
            application.Dispose();
        }
    }
}

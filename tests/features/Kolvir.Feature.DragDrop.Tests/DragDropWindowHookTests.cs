using Kolvir;
using Kolvir.Backend.Headless;
using Kolvir.DragDrop;

namespace Kolvir.DragDrop.Tests;

/// <summary>
/// The portable half of the feature's window hook: it installs the router when a window is created
/// and lets go of it when the window closes, and it does nothing at all at the phases in between.
/// </summary>
/// <remarks>
/// The router it installs is internal, so what is asserted here is the contract the hook itself
/// documents - a creation installs, a close releases, the other phases are inert, and releasing a
/// window that was never installed is not an error - plus that the hook leaves the window's own
/// state alone. Driving a drag end to end needs the platform hook, which is what the Windows, macOS
/// and GTK4 runs of the demo application are for.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("DragDrop")]
public sealed class DragDropWindowHookTests
{
    private static void WithWindow(Action<Window> body)
    {
        var app = new Application(new HeadlessApplicationBackend());

        try
        {
            body(new Window("drag", new Panel()));
        }
        finally
        {
            app.Dispose();
        }
    }

    [TestMethod]
    public void TheHookRejectsANullWindow()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new DragDropWindowHook().OnPhase(WindowPhase.Created, null!));
    }

    [TestMethod]
    public void TheHookInstallsAndReleasesWithoutTouchingTheWindow()
    {
        WithWindow(window =>
        {
            var hook = new DragDropWindowHook();
            var panel = (Panel)window.Content!;

            panel.AllowDrop = true;

            hook.OnPhase(WindowPhase.Created, window);
            Assert.IsTrue(panel.AllowDrop, "installing the router does not change what the content accepts");

            hook.OnPhase(WindowPhase.Closing, window);
            Assert.IsTrue(panel.AllowDrop, "nor does releasing it");

            hook.OnPhase(WindowPhase.Disposed, window);
        });
    }

    [TestMethod]
    public void ThePhasesBetweenACreationAndACloseAreInert()
    {
        WithWindow(window =>
        {
            var hook = new DragDropWindowHook();
            var content = window.Content;

            hook.OnPhase(WindowPhase.Created, window);

            hook.OnPhase(WindowPhase.Showing, window);
            hook.OnPhase(WindowPhase.Shown, window);
            hook.OnPhase(WindowPhase.Closing, window);

            Assert.IsFalse(window.IsClosed, "the hook is not what closes a window");
            Assert.AreSame(content, window.Content, "and it does not rearrange the window's content");
        });
    }

    [TestMethod]
    public void ReleasingAWindowThatWasNeverInstalledIsHarmless()
    {
        WithWindow(window =>
        {
            var hook = new DragDropWindowHook();

            hook.OnPhase(WindowPhase.Closing, window);
            hook.OnPhase(WindowPhase.Disposed, window);
        });
    }
}

using XPlatUI;
using XPlatUI.Backend.Headless;
using XPlatUI.DragDrop;

namespace XPlatUI.DragDrop.Tests;

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
    private static void WithWindow(Action<XPlatWindow> body)
    {
        var app = new XPlatApplication(new HeadlessApplicationBackend());

        try
        {
            body(new XPlatWindow("drag", new XPlatPanel()));
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
            () => new XPlatDragDropWindowHook().OnPhase(XPlatWindowPhase.Created, null!));
    }

    [TestMethod]
    public void TheHookInstallsAndReleasesWithoutTouchingTheWindow()
    {
        WithWindow(window =>
        {
            var hook = new XPlatDragDropWindowHook();
            var panel = (XPlatPanel)window.Content!;

            panel.AllowDrop = true;

            hook.OnPhase(XPlatWindowPhase.Created, window);
            Assert.IsTrue(panel.AllowDrop, "installing the router does not change what the content accepts");

            hook.OnPhase(XPlatWindowPhase.Closing, window);
            Assert.IsTrue(panel.AllowDrop, "nor does releasing it");

            hook.OnPhase(XPlatWindowPhase.Disposed, window);
        });
    }

    [TestMethod]
    public void ThePhasesBetweenACreationAndACloseAreInert()
    {
        WithWindow(window =>
        {
            var hook = new XPlatDragDropWindowHook();
            var content = window.Content;

            hook.OnPhase(XPlatWindowPhase.Created, window);

            hook.OnPhase(XPlatWindowPhase.Showing, window);
            hook.OnPhase(XPlatWindowPhase.Shown, window);
            hook.OnPhase(XPlatWindowPhase.Closing, window);

            Assert.IsFalse(window.IsClosed, "the hook is not what closes a window");
            Assert.AreSame(content, window.Content, "and it does not rearrange the window's content");
        });
    }

    [TestMethod]
    public void ReleasingAWindowThatWasNeverInstalledIsHarmless()
    {
        WithWindow(window =>
        {
            var hook = new XPlatDragDropWindowHook();

            hook.OnPhase(XPlatWindowPhase.Closing, window);
            hook.OnPhase(XPlatWindowPhase.Disposed, window);
        });
    }
}

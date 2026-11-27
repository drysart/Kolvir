using Kolvir;
using Kolvir.Backend.Headless;

namespace Kolvir.Tests;

/// <summary>
/// A window owns the control tree it shows: closing it disposes the root, and the root's own
/// disposal takes the children with it. Without that, a closed window leaves its controls alive -
/// and, with an asset load attached to an image among them, keeps that load running at its last
/// priority for a window nobody is looking at.
/// </summary>
/// <remarks>
/// The order is as much of the contract as the disposal itself: the tree goes before the scope, so a
/// control's <see cref="Control.AddDisposingHandler"/> handler can still resolve a window-scoped
/// service, and after the closed handlers, so a closed handler still sees the controls it was shown.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class WindowContentLifetimeTests
{
    [TestMethod]
    public void ClosingAWindowDisposesItsContentTree()
    {
        var child = new Panel();
        var root = new Panel();
        root.Controls.Add(child);

        using var scene = HeadlessScene.Create(root);

        int rootDisposals = 0, childDisposals = 0;
        root.AddDisposingHandler(_ => rootDisposals++);
        child.AddDisposingHandler(_ => childDisposals++);

        scene.Window.Close();

        Assert.IsTrue(scene.Window.IsClosed, "the window is closed");
        Assert.AreEqual(1, rootDisposals, "the root of the tree is disposed");
        Assert.AreEqual(1, childDisposals, "and disposing the root disposes the children");
    }

    [TestMethod]
    public void AClosedWindowHandsBackNoContent()
    {
        var root = new Panel();
        using var scene = HeadlessScene.Create(root);

        scene.Window.Close();

        Assert.IsNull(scene.Window.Content, "the window hands back no disposed control");
        Assert.IsNull(scene.Host.Root, "and the host is not left pointing at it");
        Assert.IsFalse(scene.Window.TryGetControlById("anything", out _), "a closed window finds no controls");
    }

    [TestMethod]
    public void TheClosedHandlersStillSeeTheTree()
    {
        var label = new Label("text");
        using var scene = HeadlessScene.Create(label);

        bool disposedAtClose = false;
        int disposals = 0;

        label.AddDisposingHandler(_ => disposals++);
        scene.Window.AddClosedHandler(_ => disposedAtClose = disposals != 0);

        scene.Window.Close();

        Assert.IsFalse(disposedAtClose, "a closed handler still sees the controls it was shown");
        Assert.AreEqual(1, disposals, "and the tree is disposed once every closed handler has run");
    }

    [TestMethod]
    public void AControlCanResolveAScopedServiceWhileItIsBeingDisposed()
    {
        var marker = new Marker();
        var root = new Panel();
        Marker? resolved = null;

        using var scene = HeadlessScene.Create(
            new HeadlessApplicationBackend(),
            root,
            options: new ApplicationOptions
            {
                ApplicationName = "window-content-lifetime",
                ConfigureServices = services => services.AddWindowScoped<Marker>((_, _) => marker),
            });

        root.AddDisposingHandler(control => resolved = ((Control)control).GetService<Marker>());

        scene.Window.Close();

        Assert.AreSame(
            marker,
            resolved,
            "a control reaches the window's scope from its Disposing handler, so the tree goes first");

        Assert.ThrowsExactly<ObjectDisposedException>(
            () => scene.Window.Services.GetService(typeof(Marker)),
            "and the scope is released after it");
    }

    [TestMethod]
    public void AWindowWithNoContentCloses()
    {
        using var scene = HeadlessScene.Create();

        scene.Window.Close();

        Assert.IsTrue(scene.Window.IsClosed);
        Assert.IsNull(scene.Window.Content);
    }

    [TestMethod]
    public void DisposingTwiceDoesNotDisposeTheTreeTwice()
    {
        var root = new Panel();
        using var scene = HeadlessScene.Create(root);

        int disposals = 0;
        root.AddDisposingHandler(_ => disposals++);

        scene.Window.Dispose();
        scene.Window.Dispose();

        Assert.AreEqual(1, disposals, "the tree is disposed once, however many times the window is");
    }

    /// <summary>A service with no dependencies, so the scope's plumbing is the thing under test.</summary>
    private sealed class Marker;
}

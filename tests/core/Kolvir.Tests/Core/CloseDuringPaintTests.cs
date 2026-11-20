using Kolvir;
using Kolvir.Backend.Headless;
using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// A window cannot be torn down while a host is inside <see cref="UiHost.Paint"/>: the backend
/// still owns the surface it handed to the paint, and disposing it there is a native use-after-free.
/// </summary>
/// <remarks>
/// These tests assert the ordering rather than only the outcome. A deferred close is indistinguishable
/// from a working one once the loop turn has run, so each test checks that the backend is still alive
/// when the paint returns and only then that the deferred turn closes it.
/// </remarks>
[TestClass]
public sealed class CloseDuringPaintTests
{
    [TestMethod]
    public void ClosingAWindowFromItsOwnFrameDefersTheTeardown()
    {
        using var scene = HeadlessScene.Create(new Panel { Dock = DockStyle.Fill }, 100f, 80f);
        var backend = (HeadlessApplicationBackend)scene.Backend;

        scene.WindowBackend.Show();

        bool requested = false;
        int closed = 0;
        scene.Window.AddClosedHandler(_ => closed++);

        using var token = UiHost.AddFrameStartingHandler(host =>
        {
            if (requested || !ReferenceEquals(host, scene.Host))
            {
                return;
            }

            requested = true;
            scene.Window.Close();

            Assert.IsTrue(scene.Window.IsClosed, "the model window is closed at once");
            Assert.AreEqual(0, closed, "the teardown has not run inside the paint");
            Assert.IsTrue(scene.WindowBackend.IsVisible, "the backend surface is still alive inside the paint");
        });

        scene.Paint();

        Assert.IsTrue(requested, "the frame-starting handler ran");
        Assert.AreEqual(0, closed, "no teardown happened between Paint starting and returning");
        Assert.IsTrue(scene.WindowBackend.IsVisible, "the backend is still alive after the paint");

        backend.DrainPending();

        Assert.AreEqual(1, closed, "the deferred loop turn closed the window");
        Assert.IsFalse(scene.WindowBackend.IsVisible, "and disposed the backend after the paint");
    }

    [TestMethod]
    public void PaintingOneWindowCanCloseAnotherAndTheClosedWindowStillPaints()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(panel, 100f, 80f);
        var backend = (HeadlessApplicationBackend)scene.Backend;

        scene.WindowBackend.Show();

        using var other = new Window("other", new Panel { Dock = DockStyle.Fill })
        {
            ClientSize = new SKSize(100f, 80f),
        };

        var otherBackend = (HeadlessWindowBackend)other.Backend;
        otherBackend.Show();

        bool requested = false;
        int closed = 0;
        scene.Window.AddClosedHandler(_ => closed++);

        using var token = UiHost.AddFrameStartingHandler(host =>
        {
            if (requested || !ReferenceEquals(host, otherBackend.Host))
            {
                return;
            }

            requested = true;
            scene.Window.Close();

            Assert.IsTrue(scene.Window.IsClosed);
            Assert.AreEqual(0, closed, "the other window's teardown waits for the loop turn");
            Assert.IsTrue(scene.WindowBackend.IsVisible);
        });

        using (var bitmap = new SKBitmap(100, 80))
        using (var canvas = new SKCanvas(bitmap))
        {
            otherBackend.RenderTo(canvas);
        }

        Assert.IsTrue(requested, "painting the other window ran the handler");
        Assert.AreEqual(0, closed, "the close was deferred, not run from the other window's paint");
        Assert.IsTrue(scene.WindowBackend.IsVisible);

        // The exact shape of the reported crash: paint the window whose close is now pending.
        scene.Paint();
        Assert.AreEqual(0, closed, "the pending close did not run from the second paint either");

        backend.DrainPending();

        Assert.AreEqual(1, closed, "the deferred turn closed it once");
        Assert.IsFalse(scene.WindowBackend.IsVisible);
    }

    [TestMethod]
    public void AClientSizeAssignedToAShownWindowDuringAFrameIsAppliedAfterIt()
    {
        using var scene = HeadlessScene.Create(new Panel { Dock = DockStyle.Fill }, 100f, 80f);
        var backend = (HeadlessApplicationBackend)scene.Backend;

        bool resized = false;

        using var token = UiHost.AddFrameStartingHandler(host =>
        {
            if (resized || !ReferenceEquals(host, scene.Host))
            {
                return;
            }

            resized = true;
            scene.Window.ClientSize = new SKSize(200f, 120f);

            Assert.AreEqual(100f, scene.WindowBackend.ClientSize.Width, 0.01f,
                "the surface is not reallocated inside the paint");
            Assert.AreEqual(80f, scene.WindowBackend.ClientSize.Height, 0.01f);
        });

        scene.RunWhileShown(() =>
        {
            scene.Paint();

            Assert.IsTrue(resized);
            Assert.AreEqual(100f, scene.WindowBackend.ClientSize.Width, 0.01f,
                "the size waits for a loop turn outside the paint");
            Assert.AreEqual(200f, scene.Window.ClientSize.Width, 0.01f,
                "the window reports the size it was assigned");

            backend.DrainPending();

            Assert.AreEqual(200f, scene.WindowBackend.ClientSize.Width, 0.01f,
                "the deferred turn applied the size");
            Assert.AreEqual(120f, scene.WindowBackend.ClientSize.Height, 0.01f);

            scene.Application.Exit();
        });
    }

    [TestMethod]
    public void AWindowCreatedDuringAPaintTakesItsGeometryImmediately()
    {
        using var scene = HeadlessScene.Create(new Panel { Dock = DockStyle.Fill }, 100f, 80f);

        Window? created = null;

        using var token = UiHost.AddFrameStartingHandler(host =>
        {
            if (created is not null || !ReferenceEquals(host, scene.Host))
            {
                return;
            }

            created = new Window("created", new Panel { Dock = DockStyle.Fill });
            created.ClientSize = new SKSize(240f, 140f);
            created.Location = new SKPoint(30f, 40f);
        });

        scene.Paint();

        Assert.IsNotNull(created, "the frame created the window");
        var window = created!;

        Assert.AreEqual(new SKSize(240f, 140f), window.Backend.ClientSize,
            "a window with no surface yet takes its assigned size immediately");
        Assert.AreEqual(new SKPoint(30f, 40f), window.Backend.Location,
            "and its assigned location immediately");

        window.Dispose();
    }

    [TestMethod]
    public void DisposingTheApplicationFromAFrameDefersItsTeardown()
    {
        using var scene = HeadlessScene.Create(new Panel { Dock = DockStyle.Fill }, 100f, 80f);
        var backend = (HeadlessApplicationBackend)scene.Backend;

        // Registers the window with the application without showing it, so the application owns it
        // when it tears down. The backend is made visible separately because it is what the paint
        // path reports on.
        scene.Window.Show();
        scene.WindowBackend.Show();

        bool requested = false;
        int closed = 0;
        scene.Window.AddClosedHandler(_ => closed++);

        using var token = UiHost.AddFrameStartingHandler(host =>
        {
            if (requested || !ReferenceEquals(host, scene.Host))
            {
                return;
            }

            requested = true;
            scene.Application.Dispose();

            Assert.AreEqual(0, closed, "the application did not tear its windows down inside the paint");
            Assert.IsTrue(scene.WindowBackend.IsVisible);
        });

        scene.Paint();

        Assert.IsTrue(requested);
        Assert.AreEqual(0, closed, "the application teardown waits for the loop turn");
        Assert.IsTrue(scene.WindowBackend.IsVisible);

        backend.DrainPending();

        Assert.AreEqual(1, closed, "the deferred turn tore the application down");
        Assert.IsFalse(scene.WindowBackend.IsVisible);
    }
}

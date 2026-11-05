using SkiaSharp;
using XPlatUI;
using XPlatUI.Backend.Headless;

namespace XPlatUI.Tests;

/// <summary>
/// The suite's own foundation: the scene really does build an application and a window, and the
/// headless backend really does lay out and paint without a display.
/// </summary>
/// <remarks>
/// These are the tests whose failure means the other four hundred are lying, so they assert the
/// properties every other test assumes rather than a control's behaviour.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class HeadlessSceneTests : HeadlessTestBase
{
    [TestMethod]
    public void ASceneBuildsAWindowWithALaidOutRoot()
    {
        var root = new XPlatPanel { Dock = DockStyle.Fill };

        using var scene = HeadlessScene.Create(root, 320f, 200f);

        Assert.AreSame(root, scene.Host.Root, "the host should own the window's content");
        Assert.AreEqual(new SKSize(320f, 200f), root.Size, "the root should fill the client area");
        Assert.AreSame(scene.Window, root.Window, "a control should find its window through the host");
    }

    [TestMethod]
    public void TheClientSizeDrivesLayoutOnAResize()
    {
        var root = new XPlatPanel { Dock = DockStyle.Fill };

        using var scene = HeadlessScene.Create(root, 320f, 200f);
        scene.Resize(640f, 400f);

        Assert.AreEqual(new SKSize(640f, 400f), scene.ClientSize, "the backend should report the new size");
        Assert.AreEqual(new SKSize(640f, 400f), root.Size, "the root should follow the client size");
    }

    [TestMethod]
    public void PaintingProducesAFrameAtTheClientSize()
    {
        var root = new XPlatPanel { Dock = DockStyle.Fill };

        using var scene = HeadlessScene.Create(root, 200f, 100f);
        using var bitmap = scene.Render();

        Assert.AreEqual(200, bitmap.Width);
        Assert.AreEqual(100, bitmap.Height);
        Assert.IsTrue(scene.Host.FrameCount > 0, "a render should count as a frame");
        Assert.AreEqual(root.Background.Color, bitmap.GetPixel(5, 5), "the panel should paint its own surface");
    }

    [TestMethod]
    public void TheDefaultScreenIsSyntheticAndNotTheDesktop()
    {
        using var scene = HeadlessScene.Create();

        var screen = scene.Backend.GetScreens().Single();

        Assert.IsTrue(screen.IsPrimary);
        Assert.AreEqual(1f, screen.ScaleFactor);
        Assert.AreEqual(HeadlessApplicationBackend.DefaultScreenSize, screen.Bounds.Size);
    }

    [TestMethod]
    public void RunShowsTheWindowsAndTearsDownWithoutALoop()
    {
        var backend = new HeadlessApplicationBackend();
        using var application = new XPlatApplication(backend);
        var window = new XPlatWindow("run", new XPlatPanel { Dock = DockStyle.Fill });
        bool shown = false;
        window.AddShownHandler(_ => shown = true);

        window.Show();
        int exitCode = application.Run();

        Assert.AreEqual(0, exitCode);
        Assert.IsTrue(shown, "a window shown before Run should become visible inside it");
        Assert.IsTrue(window.IsClosed, "Run has no loop to return from, so it tears the application down");
        Assert.ThrowsExactly<InvalidOperationException>(() => application.Run());
    }

    [TestMethod]
    public void TheBackendCreatesOneWindowBackendForTheWindow()
    {
        using var scene = HeadlessScene.Create();
        var backend = (HeadlessApplicationBackend)scene.Backend;

        Assert.IsNotNull(backend.LastWindow);
        Assert.AreSame(scene.Window, backend.LastWindow.Window);
        Assert.AreSame(backend.LastWindow.Host, scene.Host);
    }

    [TestMethod]
    public void RenderingToPngWritesTheClientArea()
    {
        string path = Path.Combine(Path.GetTempPath(), $"xplatui-test-{Guid.NewGuid():N}.png");

        try
        {
            using var scene = HeadlessScene.Create(new XPlatPanel { Dock = DockStyle.Fill }, 160f, 90f);
            scene.WindowBackend.SavePng(path);

            Assert.IsTrue(File.Exists(path), "SavePng should write the file");

            using var image = SKBitmap.Decode(path);
            Assert.AreEqual(160, image.Width);
            Assert.AreEqual(90, image.Height);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

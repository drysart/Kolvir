using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using XPlatUI.Backend.Headless;

namespace XPlatUI.Animations.Tests;

/// <summary>
/// The overlay's fade-in as the feature animates it: one timeline moving the layer's
/// <see cref="XPlatControl.Opacity"/>, started by the layer becoming visible to the user.
/// </summary>
/// <remarks>
/// The layer is only <see cref="XPlatControl.IsDisplayed"/> once the window it is in is on screen,
/// which is also the rule that keeps its clock from running where nobody can see it, so every test
/// here asserts from inside the shown handler.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Animations")]
public sealed class OverlayAnimationTests : AnimationsTestBase
{
    private static readonly TimeSpan TwoHundred = TimeSpan.FromMilliseconds(200);

    [TestMethod]
    public void TheLayerFadesInWhenItStartsBlocking()
    {
        using var scene = OverlayScene.Create(TwoHundred);

        scene.RunWhileShown(() =>
        {
            var overlay = scene.Overlay;

            overlay.Visible = true;

            Assert.AreEqual(0f, overlay.Opacity, 0.000001f, "the layer starts clear rather than flashing up");
            Assert.IsTrue(
                overlay.ProcessKeyDown(XPlatKey.Tab, XPlatModifiers.None),
                "and is already holding the keyboard while it comes in - arriving is not half blocking");

            scene.Harness.Frame(100);

            Assert.IsTrue(overlay.Opacity is > 0.6f and < 0.9f, $"the fade is on its way, at {overlay.Opacity}");

            scene.Harness.Frame(120);

            Assert.AreEqual(1f, overlay.Opacity, 0.000001f, "it lands exactly on its resting opacity");
            Assert.IsFalse(scene.Harness.Scheduler.IsRunning, "and the application's clock stopped with it");
        });
    }

    [TestMethod]
    public void AHiddenLayerStopsTheClockAndGoesBackToItsRestingOpacity()
    {
        using var scene = OverlayScene.Create(TwoHundred);

        scene.RunWhileShown(() =>
        {
            var overlay = scene.Overlay;

            overlay.Visible = true;
            scene.Harness.Frame(100);

            Assert.IsTrue(overlay.Opacity < 1f, "the fade is in the air");
            Assert.IsTrue(scene.Harness.Scheduler.IsRunning);

            overlay.Visible = false;

            Assert.AreEqual(1f, overlay.Opacity, 0.000001f, "nothing is spent on a layer nobody can see");
            Assert.IsFalse(scene.Harness.Scheduler.IsRunning, "and its clock is given back");
        });
    }

    [TestMethod]
    public void ACancelledFadeLeavesTheLayerAtFullDimming()
    {
        using var scene = OverlayScene.Create(TwoHundred);

        scene.RunWhileShown(() =>
        {
            var overlay = scene.Overlay;

            overlay.Visible = true;
            scene.Harness.Frame(80);

            Assert.IsTrue(overlay.Opacity < 1f, "the fade is in the air");

            overlay.FadeDuration = null;

            Assert.AreEqual(1f, overlay.Opacity, 0.000001f, "removing the duration cannot strand it part way in");
            Assert.IsFalse(scene.Harness.Scheduler.IsRunning);
        });
    }

    [TestMethod]
    public void WithoutADurationTheLayerAppearsAtOnce()
    {
        using var scene = OverlayScene.Create();

        scene.RunWhileShown(() =>
        {
            scene.Overlay.Visible = true;

            Assert.AreEqual(1f, scene.Overlay.Opacity, 0.000001f);
            Assert.IsFalse(scene.Harness.Scheduler.IsRunning, "nothing was asked of the clock");
        });
    }

    [TestMethod]
    public void WithoutAClockTheLayerAppearsAtOnce()
    {
        using var scene = OverlayScene.Create(TwoHundred, registerScheduler: false);

        scene.RunWhileShown(() =>
        {
            scene.Overlay.Visible = true;

            Assert.AreEqual(1f, scene.Overlay.Opacity, 0.000001f, "no AddAnimations() snaps rather than hides the layer");
        });
    }

    [TestMethod]
    public void ASettingOnALayerThatIsAlreadyUpMovesNothing()
    {
        using var scene = OverlayScene.Create();

        scene.RunWhileShown(() =>
        {
            Assert.IsNull(scene.Overlay.FadeDuration, "a layer that never asked for one has none");

            scene.Overlay.Visible = true;
            scene.Overlay.FadeDuration = TwoHundred;

            Assert.AreEqual(TwoHundred, scene.Overlay.FadeDuration);
            Assert.AreEqual(1f, scene.Overlay.Opacity, 0.000001f, "there is nothing left to fade in");
            Assert.IsFalse(scene.Harness.Scheduler.IsRunning);
        });
    }

    [TestMethod]
    public void ANegativeDurationIsRejected()
    {
        using var scene = OverlayScene.Create();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => scene.Overlay.FadeDuration = TimeSpan.FromMilliseconds(-1));

        Assert.IsNull(scene.Overlay.FadeDuration);
    }

    /// <summary>
    /// One overlay over a panel in a window, an application the feature was registered with, and the
    /// harness whose clock and frames the test drives.
    /// </summary>
    private sealed class OverlayScene : IDisposable
    {
        private readonly XPlatApplication _application;
        private readonly XPlatWindow _window;

        private OverlayScene(
            AnimationHarness harness,
            XPlatApplication application,
            XPlatWindow window,
            XPlatOverlay overlay)
        {
            Harness = harness;
            _application = application;
            _window = window;
            Overlay = overlay;
        }

        public AnimationHarness Harness { get; }

        public XPlatOverlay Overlay { get; }

        /// <param name="fade">The fade duration, or null for a layer that appears at once.</param>
        /// <param name="registerScheduler">False to build an application that never opted into the feature.</param>
        public static OverlayScene Create(TimeSpan? fade = null, bool registerScheduler = true)
        {
            var harness = new AnimationHarness();

            var options = new XPlatApplicationOptions();

            if (registerScheduler)
            {
                options.ConfigureServices = services => services.AddSingleton(harness.Scheduler).AddAnimations();
            }

            var application = new XPlatApplication(new HeadlessApplicationBackend(), options);

            var overlay = new XPlatOverlay { Visible = false };

            var root = new XPlatPanel
            {
                Dock = DockStyle.Fill,
                ShowFrame = false,
                IsCard = false,
                BackColor = null,
                BorderWidth = 0f,
                CornerRadius = 0f,
                Padding = XPlatThickness.Empty,
            };

            root.Controls.Add(new XPlatLabel("Content") { Dock = DockStyle.Fill, AutoSize = false });
            root.Controls.Add(overlay);

            var window = new XPlatWindow("overlay", root) { ClientSize = new SKSize(360f, 220f) };
            window.Host.PerformLayout();

            if (fade is { } value)
            {
                overlay.FadeDuration = value;
            }

            return new OverlayScene(harness, application, window, overlay);
        }

        /// <summary>
        /// Shows the window and runs <paramref name="body"/> once it is up, which is when a control
        /// in it is <see cref="XPlatControl.IsDisplayed"/> - the only state a fade may run in.
        /// </summary>
        public void RunWhileShown(Action body)
        {
            ArgumentNullException.ThrowIfNull(body);

            _window.AddShownHandler(_ => body());
            _window.Show();
            _application.Run();
        }

        public void Dispose()
        {
            _window.Close();
            _application.Dispose();
            Harness.Dispose();
        }
    }
}

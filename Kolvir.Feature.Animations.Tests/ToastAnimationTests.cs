using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Kolvir.Backend.Headless;

namespace Kolvir.Animations.Tests;

/// <summary>
/// The toast's fade and slide as the feature animates them: one timeline moving
/// <see cref="Toast.ShowProgress"/>, which is its opacity and how far it has come in from the
/// edge it is docked against.
/// </summary>
/// <remarks>
/// <para>
/// The scheduler the tests drive is the one the application was given, so what is being proved is
/// the production path - the control asks the application for its clock - and not a stand-in beside
/// it. The clock only moves when a test moves it.
/// </para>
/// <para>
/// The edges worth pinning are the ones a transition can be interrupted in the middle of: a message
/// with no duration, a dismissal that has to finish before the message leaves the layout, a
/// dismissal cancelled halfway, an application that never called <c>AddAnimations()</c>, and the
/// countdown arriving while the dismissal is still running. Each of them ends with the message
/// either fully up or off the screen, never faded halfway and stuck.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Animations")]
public sealed class ToastAnimationTests : AnimationsTestBase
{
    private static readonly TimeSpan TwoHundred = TimeSpan.FromMilliseconds(200);

    [TestMethod]
    public void WithoutADurationTheToastComesAndGoesAtOnce()
    {
        using var scene = ToastScene.Create();

        var toast = scene.Toast;
        toast.Show();

        Assert.IsTrue(toast.IsOpen);
        Assert.IsTrue(toast.Visible, "a toast with nothing to animate is up the moment it is shown");
        Assert.AreEqual(1d, toast.ShowProgress, 0.000001d);
        Assert.AreEqual(1f, toast.Opacity, 0.000001f, "and fully opaque");
        Assert.IsFalse(scene.Harness.Scheduler.IsRunning, "no clock was asked for");

        toast.Dismiss();

        Assert.IsFalse(toast.IsOpen);
        Assert.IsFalse(toast.Visible, "and gone the moment it is dismissed");
        Assert.AreEqual(0d, toast.ShowProgress, 0.000001d);
        Assert.AreEqual(1, scene.Dismissed, "the dismissed handler ran once");
        Assert.IsFalse(scene.Harness.Scheduler.IsRunning);
    }

    [TestMethod]
    public void TheShowAnimationFadesAndLandsExactlyInPlace()
    {
        using var scene = ToastScene.Create(show: TwoHundred);

        var toast = scene.Toast;
        toast.Show();

        Assert.AreEqual(0d, toast.ShowProgress, 0.000001d, "it starts where it is coming from, not where it is going");
        Assert.AreEqual(0f, toast.Opacity, 0.000001f, "which is invisible");
        Assert.IsTrue(toast.Visible, "but already taking its room in the layout");

        scene.Harness.Frame(100);

        Assert.AreEqual(0.5d, toast.ShowProgress, 0.02d, "half the duration in, it is halfway in");
        Assert.AreEqual(toast.ShowProgress, toast.Opacity, 0.001d, "the fade and the slide are the same number");
        Assert.IsTrue(toast.Opacity is > 0.1f and < 0.9f);

        scene.Harness.Frame(120);

        Assert.AreEqual(1d, toast.ShowProgress, 0.000001d, "the animation ends on the target, not near it");
        Assert.AreEqual(1f, toast.Opacity, 0.000001f);
        Assert.IsFalse(scene.Harness.Scheduler.IsRunning, "and the application's clock stopped with it");
    }

    [TestMethod]
    public void TheDismissAnimationFinishesBeforeTheToastLeaves()
    {
        using var scene = ToastScene.Create(dismiss: TwoHundred);

        var toast = scene.Toast;
        toast.Show();

        Assert.AreEqual(1d, toast.ShowProgress, 0.000001d, "showing is instant, dismissing is the animated half");

        toast.Dismiss();

        Assert.IsFalse(toast.IsOpen, "it is on its way out");
        Assert.IsTrue(toast.Visible, "but still on the screen while it goes");
        Assert.AreEqual(1d, toast.ShowProgress, 0.000001d);
        Assert.AreEqual(0, scene.Dismissed, "and nothing has been dismissed yet");

        scene.Harness.Frame(100);

        Assert.IsTrue(toast.ShowProgress is > 0.1d and < 0.9d, "the transition is in the air");
        Assert.IsTrue(toast.Visible, "still on the screen");
        Assert.AreEqual(0, scene.Dismissed);

        scene.Harness.Frame(120);

        Assert.AreEqual(0d, toast.ShowProgress, 0.000001d, "it lands on 0 rather than one frame short of it");
        Assert.IsFalse(toast.Visible, "which is the moment the message goes");
        Assert.AreEqual(1, scene.Dismissed, "and the dismissed handler runs once, at the end");
        Assert.IsFalse(scene.Harness.Scheduler.IsRunning);
    }

    [TestMethod]
    public void ADismissalCancelledMidFlightStillTakesTheToastOffTheScreen()
    {
        using var scene = ToastScene.Create(dismiss: TwoHundred);

        var toast = scene.Toast;
        toast.Show();
        toast.Dismiss();

        scene.Harness.Frame(100);

        Assert.IsTrue(toast.ShowProgress is > 0.1d and < 0.9d, "the dismissal is in the air");
        Assert.IsTrue(toast.Visible);

        // Cancelling means taking the duration away: there is nothing left to finish the transition
        // with, and a message left at 0.5 would be exactly the half-faded toast this rule is about.
        toast.DismissDuration = null;

        Assert.AreEqual(0d, toast.ShowProgress, 0.000001d, "cancelling settles it on its target");
        Assert.IsFalse(toast.Visible, "and the message is gone");
        Assert.AreEqual(1, scene.Dismissed, "once");
        Assert.IsFalse(scene.Harness.Scheduler.IsRunning, "the clock is the application's again");

        // Nothing the cancelled transition left behind can remove it a second time.
        scene.Harness.Tick();

        Assert.AreEqual(1, scene.Dismissed);
    }

    [TestMethod]
    public void ShowingDuringADismissalTurnsRoundWithoutRemoving()
    {
        using var scene = ToastScene.Create(show: TwoHundred, dismiss: TwoHundred);

        var toast = scene.Toast;
        toast.Show();
        scene.Harness.Frame(220);

        toast.Dismiss();
        scene.Harness.Frame(100);

        double going = toast.ShowProgress;
        Assert.IsTrue(going is > 0.1d and < 0.9d, "the message is on its way out");

        toast.Show();

        Assert.IsTrue(toast.IsOpen, "the message is back");
        Assert.IsTrue(toast.Visible, "and never left the screen");
        Assert.AreEqual(going, toast.ShowProgress, 0.02d, "turning round starts from where it had got to");

        scene.Harness.Frame(220);

        Assert.AreEqual(1d, toast.ShowProgress, 0.000001d, "and it comes all the way back");
        Assert.AreEqual(0, scene.Dismissed, "the removal the dismissal was heading for is off");
    }

    [TestMethod]
    public void ACancelledShowSettlesFullyInPlace()
    {
        using var scene = ToastScene.Create(show: TwoHundred);

        var toast = scene.Toast;
        toast.Show();
        scene.Harness.Frame(80);

        Assert.IsTrue(toast.ShowProgress is > 0.1d and < 0.9d, "the message is on its way in");

        toast.ShowDuration = null;

        Assert.AreEqual(1d, toast.ShowProgress, 0.000001d, "removing the duration puts it exactly where it belongs");
        Assert.AreEqual(1f, toast.Opacity, 0.000001f);
        Assert.IsFalse(scene.Harness.Scheduler.IsRunning);
    }

    [TestMethod]
    public void WithoutAClockTheToastSnaps()
    {
        using var scene = ToastScene.Create(show: TwoHundred, dismiss: TwoHundred, registerScheduler: false);

        var toast = scene.Toast;
        toast.Show();

        Assert.AreEqual(1d, toast.ShowProgress, 0.000001d, "no AddAnimations() means no animation, not a stuck fade");
        Assert.AreEqual(1f, toast.Opacity, 0.000001f);

        toast.Dismiss();

        Assert.AreEqual(0d, toast.ShowProgress, 0.000001d);
        Assert.IsFalse(toast.Visible);
        Assert.AreEqual(1, scene.Dismissed);
    }

    [TestMethod]
    public void TheProgressNeverLeavesItsRange()
    {
        using var scene = ToastScene.Create(show: TwoHundred, dismiss: TwoHundred);

        var toast = scene.Toast;

        void Check(string moment)
        {
            Assert.IsTrue(toast.ShowProgress is >= 0d and <= 1d, $"{moment}: the progress is {toast.ShowProgress}");
            Assert.IsTrue(toast.Opacity is >= 0f and <= 1f, $"{moment}: the opacity is {toast.Opacity}");
        }

        toast.Show();
        Check("shown");

        for (int frame = 0; frame < 12; frame++)
        {
            scene.Harness.Frame(20);
            Check($"frame {frame} of the appearance");
        }

        toast.Dismiss();

        for (int frame = 0; frame < 12; frame++)
        {
            scene.Harness.Frame(20);
            Check($"frame {frame} of the dismissal");
        }

        Assert.IsFalse(toast.Visible);
        Assert.AreEqual(1, scene.Dismissed);
    }

    [TestMethod]
    public void ADismissingToastTakesNoTabStopOrFocus()
    {
        using var scene = ToastScene.Create(dismiss: TwoHundred);

        var toast = scene.Toast;
        toast.Show();

        toast.Focus();
        Assert.IsTrue(toast.IsFocused, "the message can hold the keyboard while it is up");

        toast.Dismiss();

        // The target is what the message is doing, so it is out of reach from the moment it is set
        // rather than from the moment the transition ends.
        Assert.IsTrue(toast.ShowProgress > 0.9d, "the transition has not moved yet");
        Assert.IsFalse(toast.TabStop, "and it is not a Tab stop on its way out");
        Assert.IsFalse(toast.Focusable);
        Assert.IsFalse(ReferenceEquals(toast, scene.Host.FocusedControl), "nor does it still hold the focus");

        scene.Harness.Frame(220);

        Assert.IsFalse(toast.Visible);
        Assert.IsFalse(toast.TabStop, "the end of the transition changes nothing");

        toast.Show();

        Assert.IsTrue(toast.TabStop, "and coming back gives it its flags back");
        Assert.IsTrue(toast.Focusable);
    }

    [TestMethod]
    public void TheCountdownArrivingDuringADismissalRemovesTheToastOnce()
    {
        using var scene = ToastScene.Create(dismiss: TwoHundred, countdown: TimeSpan.FromSeconds(4));
        var backend = scene.Backend;

        scene.RunWhileShown(() =>
        {
            var toast = scene.Toast;
            toast.Show();
            scene.Harness.Frame(220);

            Assert.IsTrue(backend.Timer.IsRunning, "an open toast is counting down");
            Assert.AreEqual(TimeSpan.FromSeconds(4), backend.Timer.Interval);

            // The close glyph is pressed while the countdown is still armed. The dismissal starts and
            // the message is still on the screen; the countdown has already been stopped by it.
            toast.Dismiss();
            scene.Harness.Frame(100);

            Assert.IsTrue(toast.Visible, "the dismissal is in the air");
            Assert.AreEqual(0, scene.Dismissed);

            // The armed tick arrives anyway - a timer the platform had already queued. It must not
            // start a second dismissal or a second removal.
            backend.Timer.Fire();
            backend.Drain();

            Assert.AreEqual(0, scene.Dismissed, "a tick after the fact dismisses nothing");

            scene.Harness.Frame(120);

            Assert.IsFalse(toast.Visible, "the transition finishes the job the countdown would have done");
            Assert.AreEqual(1, scene.Dismissed, "one removal, not two");
            Assert.IsFalse(backend.Timer.IsRunning);

            backend.Timer.Fire();
            backend.Drain();

            Assert.AreEqual(1, scene.Dismissed);
        });
    }

    [TestMethod]
    public void ANegativeDurationIsRejected()
    {
        using var scene = ToastScene.Create();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => scene.Toast.ShowDuration = TimeSpan.FromMilliseconds(-1));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => scene.Toast.DismissDuration = TimeSpan.FromMilliseconds(-1));

        Assert.IsNull(scene.Toast.ShowDuration, "a rejected value is not stored");
        Assert.IsNull(scene.Toast.DismissDuration);
    }

    /// <summary>
    /// One toast docked to the bottom of a laid-out window, an application the feature was registered
    /// with, and the harness whose clock and frames the test drives.
    /// </summary>
    private sealed class ToastScene : IDisposable
    {
        private readonly Application _application;
        private readonly Window _window;

        private ToastScene(
            AnimationHarness harness,
            ManualTimerApplicationBackend backend,
            Application application,
            Window window,
            Toast toast,
            UiHost host)
        {
            Harness = harness;
            Backend = backend;
            _application = application;
            _window = window;
            Toast = toast;
            Host = host;
        }

        public AnimationHarness Harness { get; }

        public ManualTimerApplicationBackend Backend { get; }

        public Toast Toast { get; }

        public UiHost Host { get; }

        /// <summary>How many times the message reported that it had gone.</summary>
        public int Dismissed { get; private set; }

        /// <param name="show">The appearance duration, or null for a message that appears at once.</param>
        /// <param name="dismiss">The dismissal duration, or null for one that goes at once.</param>
        /// <param name="registerScheduler">False to build an application that never opted into the feature.</param>
        /// <param name="countdown">The toast's own duration; null keeps it up until it is dismissed.</param>
        public static ToastScene Create(
            TimeSpan? show = null,
            TimeSpan? dismiss = null,
            bool registerScheduler = true,
            TimeSpan? countdown = null)
        {
            var harness = new AnimationHarness();
            var backend = new ManualTimerApplicationBackend();

            var options = new ApplicationOptions();

            if (registerScheduler)
            {
                options.ConfigureServices = services => services.AddSingleton(harness.Scheduler).AddAnimations();
            }

            var application = new Application(backend, options);

            var toast = new Toast("Saved") { Dock = DockStyle.Bottom, Duration = countdown };

            var root = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = null,
                Padding = Thickness.Empty,
            };

            root.Controls.Add(new Label("Content") { Dock = DockStyle.Fill, AutoSize = false });
            root.Controls.Add(toast);

            var window = new Window("toast", root) { ClientSize = new SKSize(360f, 200f) };
            window.Host.PerformLayout();

            if (show is { } showValue)
            {
                toast.ShowDuration = showValue;
            }

            if (dismiss is { } dismissValue)
            {
                toast.DismissDuration = dismissValue;
            }

            var scene = new ToastScene(harness, backend, application, window, toast, window.Host);
            toast.AddDismissedHandler(_ => scene.Dismissed++);

            return scene;
        }

        /// <summary>
        /// Shows the window and runs <paramref name="body"/> once it is up, which is the only moment
        /// a control is really <see cref="Control.IsDisplayed"/> - and so the only moment the
        /// countdown runs. <see cref="Application.Run"/> tears the application down when it
        /// returns, so the body asserts inside the handler.
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

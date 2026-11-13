using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Kolvir.Backend.Headless;

namespace Kolvir.Animations.Tests;

/// <summary>
/// The expander's fold as the feature animates it: one timeline moving
/// <see cref="Expander.ExpandProgress"/>, which is what turns the arrow and takes the height
/// with it.
/// </summary>
/// <remarks>
/// <para>
/// The scheduler the tests drive is the one the application was given, so what is being proved is
/// the production path - the control asks the application for its clock - and not a stand-in beside
/// it. The clock only moves when a test moves it.
/// </para>
/// <para>
/// The four edges worth pinning are the ones a fold can be interrupted in the middle of: a duration
/// set on a section that is already where it belongs, a collapse started while the section is still
/// opening, a duration removed mid-flight, and an application that never called
/// <c>AddAnimations()</c>. Each of them ends with the section exactly on its target rather than
/// somewhere in between.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Animations")]
public sealed class ExpanderAnimationTests : AnimationsTestBase
{
    private static readonly TimeSpan TwoHundred = TimeSpan.FromMilliseconds(200);

    [TestMethod]
    public void TheFoldFollowsTheAnimationFrameByFrame()
    {
        using var scene = AnimatedScene.Create(TwoHundred);

        var expander = scene.Expander;
        Control child = scene.Child;
        float open = expander.Height;

        expander.IsExpanded = false;

        Assert.AreEqual(1d, expander.ExpandProgress, 0.001d, "the fold starts where the section is, not at the target");

        scene.Harness.Frame(100);

        Assert.AreEqual(0.5d, expander.ExpandProgress, 0.02d, "half the duration in, the section is half folded");
        Assert.AreEqual(45d, expander.ArrowRotationDegrees, 1.8d, "and the arrow has turned with it");
        Assert.IsTrue(expander.Height < open, "the height came down with the progress");
        Assert.IsTrue(expander.Height > scene.HeaderHeight, "and is not yet the header");
        Assert.IsTrue(child.Bounds.Height > 0f, "the content is still showing what fits");
    }

    [TestMethod]
    public void TheFoldLandsExactlyOnTheTarget()
    {
        using var scene = AnimatedScene.Create(TwoHundred);

        var expander = scene.Expander;
        float header = scene.HeaderHeight;

        expander.IsExpanded = false;
        scene.Harness.Frame(200);

        Assert.AreEqual(0d, expander.ExpandProgress, 0.000001d, "the animation ends on the target, not near it");
        Assert.AreEqual(header, expander.Height, 0.5f);
        Assert.AreEqual(0d, expander.ArrowRotationDegrees, 0.000001d, "the arrow points right again");
        Assert.IsFalse(scene.Harness.Scheduler.IsRunning, "and the application's clock stopped with it");

        expander.IsExpanded = true;
        scene.Harness.Frame(200);

        Assert.AreEqual(1d, expander.ExpandProgress, 0.000001d, "the same the other way round");
        Assert.AreEqual(90d, expander.ArrowRotationDegrees, 0.000001d);
        Assert.IsFalse(scene.Harness.Scheduler.IsRunning);
    }

    [TestMethod]
    public void ACollapseStartedMidExpandTurnsRoundFromWhereItIs()
    {
        using var scene = AnimatedScene.Create(TwoHundred);

        var expander = scene.Expander;

        expander.IsExpanded = false;
        scene.Harness.Frame(80);
        double folding = expander.ExpandProgress;

        Assert.IsTrue(folding is > 0.1d and < 0.9d, "the section is in the middle of a fold");

        // What the control must not do is jump the progress to the new target and come back: the
        // handler is the record of every value it was moved to, and the target is not one of them.
        var moved = new List<double>();
        expander.AddExpandProgressChangedHandler(sender =>
            moved.Add(((Expander)sender).ExpandProgress));

        expander.IsExpanded = true;

        Assert.IsFalse(moved.Any(value => value >= 0.999d), "the control never snapped it to open first");
        Assert.AreEqual(folding, expander.ExpandProgress, 0.02d, "turning round starts from where it had got to");

        scene.Harness.Frame(40);
        double turnedRound = expander.ExpandProgress;

        Assert.IsTrue(turnedRound > folding, "and it is on its way back up");

        scene.Harness.Frame(200);

        Assert.AreEqual(1d, expander.ExpandProgress, 0.000001d, "reaching the target it turned towards");
    }

    [TestMethod]
    public void RemovingTheDurationMidFlightSettlesTheSection()
    {
        using var scene = AnimatedScene.Create(TwoHundred);

        var expander = scene.Expander;

        expander.IsExpanded = false;
        scene.Harness.Frame(100);

        Assert.IsTrue(expander.ExpandProgress is > 0.1d and < 0.9d, "the fold is in the air");

        expander.ExpandDuration = null;

        Assert.AreEqual(0d, expander.ExpandProgress, 0.000001d, "removing the duration lands it on its target");
        Assert.IsFalse(scene.Harness.Scheduler.IsRunning, "and gives the application its clock back");
    }

    [TestMethod]
    public void WithoutAClockTheSectionSnaps()
    {
        using var scene = AnimatedScene.Create(TwoHundred, registerScheduler: false);

        var expander = scene.Expander;
        float open = expander.Height;

        expander.IsExpanded = false;

        Assert.AreEqual(0d, expander.ExpandProgress, 0.000001d, "no AddAnimations() means no animation, not a stuck fold");
        Assert.AreEqual(scene.HeaderHeight, expander.Height, 0.5f);

        expander.IsExpanded = true;

        Assert.AreEqual(1d, expander.ExpandProgress, 0.000001d);
        Assert.AreEqual(open, expander.Height, 0.5f);
    }

    [TestMethod]
    public void ADurationSetOnASectionThatIsAlreadyThereMovesNothing()
    {
        using var scene = AnimatedScene.Create();

        var expander = scene.Expander;

        Assert.IsNull(expander.ExpandDuration, "an expander that never asked for one has none");

        expander.ExpandDuration = TwoHundred;

        Assert.AreEqual(TwoHundred, expander.ExpandDuration);
        Assert.AreEqual(1d, expander.ExpandProgress, 0.000001d, "there is no fold to animate");
        Assert.IsFalse(scene.Harness.Scheduler.IsRunning, "so no clock starts");

        expander.IsExpanded = false;
        scene.Harness.Frame(100);

        Assert.IsTrue(expander.ExpandProgress is > 0.1d and < 0.9d, "the next fold does use it");
    }

    [TestMethod]
    public void ANegativeDurationIsRejected()
    {
        using var scene = AnimatedScene.Create();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => scene.Expander.ExpandDuration = TimeSpan.FromMilliseconds(-1));
    }

    [TestMethod]
    public void ACollapsingSectionTakesNoNewFocusWhileItFolds()
    {
        using var scene = AnimatedScene.Create(TwoHundred);

        var expander = scene.Expander;

        scene.Child.Focus();
        Assert.IsTrue(scene.Child.IsFocused);

        expander.IsExpanded = false;

        // The target is set, the frame has not run: the section is still drawn nearly whole, and
        // the keyboard has already left it. That is the rule - the target decides, from the moment
        // it is set - rather than the fold deciding as it goes.
        Assert.IsTrue(expander.ExpandProgress > 0.9d, "the fold has not moved yet");
        Assert.AreSame(expander, scene.Host.FocusedControl, "and the focus still went to the header");

        scene.Host.ProcessKeyDown(Key.Tab);

        Assert.AreSame(scene.After, scene.Host.FocusedControl, "a fold in progress is stepped over");

        scene.Harness.Frame(200);

        Assert.AreEqual(0d, expander.ExpandProgress, 0.000001d);
        Assert.AreSame(scene.After, scene.Host.FocusedControl, "and the end of the fold changes nothing");
    }

    /// <summary>
    /// One expander in a laid-out window, an application the feature was registered with, and the
    /// harness whose clock and frames the test drives.
    /// </summary>
    private sealed class AnimatedScene : IDisposable
    {
        private readonly Application _application;
        private readonly UiHost _host;

        private AnimatedScene(
            AnimationHarness harness,
            Application application,
            UiHost host,
            Expander expander,
            Control child,
            Button after,
            float headerHeight)
        {
            Harness = harness;
            _application = application;
            _host = host;
            Expander = expander;
            Child = child;
            After = after;
            HeaderHeight = headerHeight;
        }

        public AnimationHarness Harness { get; }

        public Expander Expander { get; }

        public Control Child { get; }

        /// <summary>The control after the section in the Tab order.</summary>
        public Button After { get; }

        /// <summary>The host of the window, for the tests that drive the keyboard.</summary>
        public UiHost Host => _host;

        public float HeaderHeight { get; }

        /// <param name="duration">The fold's duration, or null for a section that animates nothing.</param>
        /// <param name="registerScheduler">
        /// False to build an application that never opted into the feature, which is what has to
        /// degrade to a jump.
        /// </param>
        public static AnimatedScene Create(TimeSpan? duration = null, bool registerScheduler = true)
        {
            var harness = new AnimationHarness();

            var options = new ApplicationOptions();

            if (registerScheduler)
            {
                options.ConfigureServices = services => services.AddSingleton(harness.Scheduler).AddAnimations();
            }

            var application = new Application(new HeadlessApplicationBackend(), options);

            var expander = new Expander("Appearance")
            {
                Dock = DockStyle.Top,
                Height = 160f,
            };

            var child = new CheckBox("Show grid") { Dock = DockStyle.Top, AutoSize = true };
            expander.Controls.Add(child);

            var root = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = null,
                BorderWidth = 0f,
                CornerRadius = 0f,
                Padding = Thickness.Empty,
            };

            root.Controls.Add(expander);

            // Something after the section for the Tab order to arrive at, which is how the tests
            // see that the folded content was stepped over rather than merely never reached.
            var after = new Button("After") { Dock = DockStyle.Top, Height = 32f };
            root.Controls.Add(after);

            var window = new Window("expander", root) { ClientSize = new SKSize(420f, 320f) };
            window.Host.PerformLayout();

            // The header's height is read off the control rather than guessed: it depends on the
            // theme's font, so a test that hard-coded it would be measuring the theme.
            expander.IsExpanded = false;
            float header = expander.Height;
            expander.IsExpanded = true;

            if (duration is { } value)
            {
                expander.ExpandDuration = value;
            }

            return new AnimatedScene(harness, application, window.Host, expander, child, after, header);
        }

        public void Dispose()
        {
            _application.Dispose();
            Harness.Dispose();
        }
    }
}

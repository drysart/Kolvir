using SkiaSharp;
using XPlatUI.Undo;

namespace XPlatUI.Tests;

/// <summary>
/// The image timeline and the control's half of it: when the position moves, when it does not, and
/// when a repaint timer is allowed to exist.
/// </summary>
/// <remarks>
/// <para>
/// The whole timeline is driven by <see cref="TestClock"/> through
/// <see cref="XPlatUndoClock.Current"/>, so nothing sleeps and "position is computed, never
/// accumulated" is a claim a test can make in one line: move the clock, read the frame.
/// </para>
/// <para>
/// The timer assertions use the same manual backend the marquee progress bar's tests use, because
/// the rule is that control's rule: no clock for a control nobody can look at, and the clock is
/// scheduled to the next frame boundary rather than to a display rate.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class ImageAnimationTests : HeadlessTestBase
{
    private readonly TestClock _clock = new();

    [TestInitialize]
    public void InstallClock() => XPlatUndoClock.Current = _clock;

    [TestMethod]
    public void ThePositionAdvancesWithNoTimerAndWhileNotDisplayed()
    {
        // The headline case: a paused application, a control nobody is showing, and no tick in
        // sight. Time still passes, so the frame is the one the clock has reached.
        using var source = new StepSource([100, 200, 300, 100]);
        using var data = new XPlatImageData(source);
        var image = new XPlatImage { Dock = DockStyle.Fill, Image = data };

        using var scene = HeadlessScene.Create(image, 40f, 40f);

        // Deliberately not shown: the harness paints a window it never displayed.
        data.Play();
        _clock.Advance(350);

        Assert.AreEqual(TimeSpan.FromMilliseconds(350), data.Position, "the position follows the clock");
        Assert.AreEqual(3f, data.CurrentFrame!.CullRect.Width, "and the frame is the one 350 ms is inside");

        scene.Paint();
        Assert.IsNotNull(image.Image, "and the control is showing it");
    }

    [TestMethod]
    public void PauseFreezesAndPlayResumesFromWhereItStopped()
    {
        using var source = new StepSource([100, 200, 300, 100]);
        using var data = new XPlatImageData(source);

        data.Play();
        _clock.Advance(150);

        data.Pause();
        Assert.IsFalse(data.IsPlaying);
        Assert.AreEqual(TimeSpan.FromMilliseconds(150), data.Position, "paused where it was");

        _clock.Advance(5000);
        Assert.AreEqual(TimeSpan.FromMilliseconds(150), data.Position, "and it stays there however long time passes");

        data.Play();
        _clock.Advance(50);
        Assert.AreEqual(TimeSpan.FromMilliseconds(200), data.Position, "playing resumed from the pause, not from zero");
    }

    [TestMethod]
    public void SeekWhilePlayingReanchorsTheClock()
    {
        // Without re-anchoring, the clock that was already running would carry the old position's
        // elapsed time into the new one and the seek would appear to jump backwards.
        using var source = new StepSource([100, 200, 300, 100]);
        using var data = new XPlatImageData(source);

        data.Play();
        _clock.Advance(400);
        data.Seek(TimeSpan.FromMilliseconds(50));

        Assert.AreEqual(TimeSpan.FromMilliseconds(50), data.Position, "the seek landed where it was asked to");

        _clock.Advance(30);
        Assert.AreEqual(TimeSpan.FromMilliseconds(80), data.Position, "and playback continued from there");
    }

    [TestMethod]
    public void SeekClampsPastBothEnds()
    {
        using var source = new StepSource([100, 200, 300, 100]);
        using var data = new XPlatImageData(source);

        data.Seek(TimeSpan.FromMilliseconds(-500));
        Assert.AreEqual(TimeSpan.Zero, data.Position, "a negative seek is the first frame");

        data.Seek(TimeSpan.FromSeconds(30));
        Assert.AreEqual(Data.LastPosition(source.Duration), data.Position, "and a seek past the end is the last frame");

        data.Seek(TimeSpan.FromMilliseconds(250));
        Assert.AreEqual(TimeSpan.FromMilliseconds(250), data.Position, "and a seek inside lands exactly");
    }

    [TestMethod]
    public void ALoopingAnimationWrapsAndAOneShotStopsOnItsLastFrame()
    {
        using var looping = new XPlatImageData(new StepSource([100, 200, 300, 100]) { Loops = true });
        looping.Play();
        _clock.Advance(750);

        Assert.IsTrue(looping.IsPlaying, "a looping animation keeps playing");
        Assert.AreEqual(TimeSpan.FromMilliseconds(50), looping.Position, "and wraps past the end");
        Assert.AreEqual(1f, looping.CurrentFrame!.CullRect.Width, "to the frame that position is inside");

        _clock.Reset();

        using var once = new XPlatImageData(new StepSource([100, 200, 300, 100]) { Loops = false });
        once.Play();
        _clock.Advance(2000);

        Assert.IsFalse(once.IsPlaying, "a one-shot animation stops");
        Assert.AreEqual(Data.LastPosition(once.Duration), once.Position, "on the last position in the pass");
        Assert.AreEqual(4f, once.CurrentFrame!.CullRect.Width, "which is the last frame");

        once.Play();
        _clock.Advance(100);
        Assert.IsTrue(once.IsPlaying, "and playing it at the end restarts it");
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), once.Position, "from the beginning");
    }

    [TestMethod]
    public void AStillImageNeverPlaysAndNeverMoves()
    {
        using var picture = Picture(7);
        using var data = XPlatImageData.FromPicture(picture, disposePictureWithData: true);

        Assert.AreEqual(TimeSpan.Zero, data.Duration, "a picture has no timeline");
        Assert.IsNull(data.TimeUntilNextFrame(TimeSpan.Zero), "and nothing to wait for");

        data.Play();
        _clock.Advance(1000);

        Assert.IsFalse(data.IsPlaying, "Play is a no-op for a still image");
        Assert.AreEqual(TimeSpan.Zero, data.Position, "and the position stays at zero");

        data.Seek(TimeSpan.FromSeconds(5));
        data.Pause();
        Assert.AreEqual(TimeSpan.Zero, data.Position, "as does Seek, rather than throwing");
    }

    [TestMethod]
    public void APlayingAnimationInAShownWindowRunsATimerScheduledToItsNextFrame()
    {
        var backend = new TestApplicationBackend();
        using var source = new StepSource([100, 200, 300, 100]);
        using var data = new XPlatImageData(source) { Loop = true };
        var image = new XPlatImage { Dock = DockStyle.Fill, Image = data };

        using var scene = HeadlessScene.Create(backend, image, 40f, 40f);

        scene.RunWhileShown(() =>
        {
            Assert.IsFalse(backend.Timer.IsRunning, "a paused animation has no timer");

            data.Play();

            Assert.IsTrue(backend.Timer.IsRunning, "playing it in a shown window starts one");
            Assert.AreEqual(
                TimeSpan.FromMilliseconds(100),
                backend.Timer.Interval,
                "scheduled to the next frame boundary, not to a display rate");
        });
    }

    [TestMethod]
    public void PausingHidingAndAStillImageAllStopTheTimer()
    {
        var backend = new TestApplicationBackend();
        using var source = new StepSource([100, 200, 300, 100]);
        using var data = new XPlatImageData(source) { Loop = true };
        var image = new XPlatImage { Dock = DockStyle.Fill, Image = data };

        using var scene = HeadlessScene.Create(backend, image, 40f, 40f);

        scene.RunWhileShown(() =>
        {
            data.Play();
            Assert.IsTrue(backend.Timer.IsRunning);

            data.Pause();
            Assert.IsFalse(backend.Timer.IsRunning, "a paused animation repaints nothing");

            data.Play();
            Assert.IsTrue(backend.Timer.IsRunning);

            image.Visible = false;
            Assert.IsFalse(backend.Timer.IsRunning, "a hidden control holds no clock");

            image.Visible = true;
            Assert.IsTrue(backend.Timer.IsRunning, "and showing it again resumes the repaints");

            // The marquee rule again: a still image has nothing to animate, so no timer at all.
            using var picture = Picture(7);
            image.Image = XPlatImageData.FromPicture(picture, disposePictureWithData: true);
            Assert.IsFalse(backend.Timer.IsRunning, "a still image has no timer");

            image.Image = data;
            Assert.IsTrue(backend.Timer.IsRunning, "and an animation put back in its place does");
        });
    }

    [TestMethod]
    public void ATickOnlyRepaintsSoTimeHiddenStillCounts()
    {
        // A control scrolled out of view and back must land on the frame time says, not on the one it
        // left on, because nothing advances the position except the clock.
        var backend = new TestApplicationBackend();
        using var source = new StepSource([100, 200, 300, 100]) { Loops = true };
        using var data = new XPlatImageData(source);
        var image = new XPlatImage { Dock = DockStyle.Fill, Image = data };

        using var scene = HeadlessScene.Create(backend, image, 40f, 40f);

        scene.RunWhileShown(() =>
        {
            data.Play();
            Assert.AreEqual(1f, data.CurrentFrame!.CullRect.Width, "the first frame");

            image.Visible = false;
            Assert.IsFalse(backend.Timer.IsRunning, "hidden, so no timer is running");

            _clock.Advance(400);

            image.Visible = true;

            Assert.AreEqual(
                3f,
                data.CurrentFrame!.CullRect.Width,
                "showing it again lands on the frame 400 ms says, not the one it left on");
        });
    }

    [TestMethod]
    public void SixtyTicksNeitherMeasureNorArrangeTheControl()
    {
        // A tick is a repaint and nothing more. A measure or an arrange per frame walks the tree on
        // the animation's clock - and inside scrolled content an arrange recomputes the extent, which
        // is how a panel ends up jumping away from the pointer that is scrolling it. Counting the
        // passes is the assertion because the visible symptom is intermittent; the count is not.
        var backend = new TestApplicationBackend();
        using var source = new StepSource([100, 200, 300, 100]) { Loops = true };
        using var data = new XPlatImageData(source);
        var image = new CountingImage { Dock = DockStyle.Fill, Image = data };

        using var scene = HeadlessScene.Create(backend, image, 40f, 40f);

        scene.RunWhileShown(() =>
        {
            scene.Paint();
            data.Play();

            int measures = image.Measures;
            int layouts = image.Layouts;
            int paints = image.Paints;

            for (int tick = 0; tick < 60; tick++)
            {
                _clock.Advance(100);

                // A timer moves its tick to the UI thread, so firing the backend only queues it:
                // draining is what a running message loop does before the next paint.
                backend.Timer.Fire();
                backend.Drain();
                scene.Paint();
            }

            Assert.AreEqual(
                measures,
                image.Measures,
                "sixty ticks measured the control zero times: a frame's size is the source's NaturalSize");
            Assert.AreEqual(
                layouts,
                image.Layouts,
                "and arranged it zero times: a tick repaints, it does not lay the tree out");
            Assert.IsTrue(image.Paints > paints, "while still repainting, which is all a tick is for");
        });
    }

    [TestMethod]
    public void AnAnimationInScrolledContentKeepsTheScrollOffsetThroughAWholeLoop()
    {
        // The same claim seen from where a user notices it: the offset of the container being
        // scrolled. The frames here differ in size and in bounds, which is what a GIF's composited
        // frames look like, so a player that measured the current frame would move this offset.
        var backend = new TestApplicationBackend();
        using var source = new StepSource([100, 200, 300, 100]) { Loops = true };
        using var data = new XPlatImageData(source);

        var column = new XPlatStackPanel
        {
            Dock = DockStyle.Fill,
            VerticalScroll = XPlatScrollStyle.Automatic,
            Spacing = 2f,
        };

        var image = new XPlatImage
        {
            AutoSize = true,
            MinimumSize = new SKSize(40f, 40f),
            Image = data,
        };

        column.Controls.Add(image);

        for (int row = 0; row < 12; row++)
        {
            column.Controls.Add(new XPlatLabel($"Filler row {row}")
            {
                AutoSize = false,
                Height = 16f,
            });
        }

        using var scene = HeadlessScene.Create(backend, column, 160f, 120f);

        scene.RunWhileShown(() =>
        {
            scene.Paint();

            column.ScrollTo(0f, 40f);
            float offset = column.VerticalOffset;
            Assert.IsTrue(offset > 0f, "the content is taller than the viewport, so the panel scrolled");

            var bounds = image.Bounds;
            data.Play();

            // Seventy ticks of a hundred milliseconds each: more than a full 700 ms loop, so every
            // frame boundary and the wrap are crossed while the panel holds an offset.
            for (int tick = 0; tick < 70; tick++)
            {
                _clock.Advance(100);
                backend.Timer.Fire();
                backend.Drain();
                scene.Paint();
            }

            Assert.AreEqual(offset, column.VerticalOffset, "the scroll offset survived a whole loop");
            Assert.AreEqual(bounds, image.Bounds, "and the picture was never resized by a frame");
        });
    }

    /// <summary>
    /// An image control that counts the passes over it, so a test can say a tick made none.
    /// </summary>
    private sealed class CountingImage : XPlatImage
    {
        internal int Measures { get; private set; }

        internal int Layouts { get; private set; }

        internal int Paints { get; private set; }

        protected override SKSize MeasureContent(SKSize availableSize)
        {
            Measures++;
            return base.MeasureContent(availableSize);
        }

        protected override void OnLayout()
        {
            Layouts++;
            base.OnLayout();
        }

        protected override void OnPaint(SKCanvas canvas, SKRect rect)
        {
            Paints++;
            base.OnPaint(canvas, rect);
        }
    }

    private static SKPicture Picture(int width)
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0f, 0f, width, width));
        using var paint = new SKPaint { Color = SKColors.Red };
        canvas.DrawRect(0f, 0f, width, width, paint);
        return recorder.EndRecording();
    }

    /// <summary>The last position inside a pass: one tick short of the duration.</summary>
    private static class Data
    {
        internal static TimeSpan LastPosition(TimeSpan duration) => TimeSpan.FromTicks(duration.Ticks - 1);
    }

    /// <summary>
    /// A source of frames whose widths are 1, 2, 3 ... so a test can name the frame it expects.
    /// </summary>
    private sealed class StepSource : IXPlatImageDataSource
    {
        private readonly SKPicture[] _frames;
        private readonly TimeSpan[] _starts;
        private readonly TimeSpan _duration;
        private bool _disposed;

        internal StepSource(int[] delaysMs)
        {
            _frames = [.. delaysMs.Select((_, index) => Picture(index + 1))];

            _starts = new TimeSpan[delaysMs.Length];
            var elapsed = TimeSpan.Zero;

            for (int index = 0; index < delaysMs.Length; index++)
            {
                _starts[index] = elapsed;
                elapsed += TimeSpan.FromMilliseconds(delaysMs[index]);
            }

            _duration = elapsed;
        }

        public bool Loops { get; init; }

        public TimeSpan Duration => _duration;

        public SKSize NaturalSize => new(_frames[0].CullRect.Width, _frames[0].CullRect.Width);

        public SKPicture FrameAt(TimeSpan position) => _frames[IndexAt(position)];

        public TimeSpan? TimeUntilNextFrame(TimeSpan position)
        {
            if (_duration <= TimeSpan.Zero)
            {
                return null;
            }

            int index = IndexAt(position);
            var boundary = index + 1 < _starts.Length ? _starts[index + 1] : _duration;
            var wait = boundary - position;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var frame in _frames)
            {
                frame.Dispose();
            }
        }

        private int IndexAt(TimeSpan position)
        {
            int index = 0;
            while (index + 1 < _starts.Length && _starts[index + 1] <= position)
            {
                index++;
            }

            return index;
        }
    }
}

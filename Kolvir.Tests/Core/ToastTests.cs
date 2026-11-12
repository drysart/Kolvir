using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The transient message bar: how it opens and dismisses itself, the pointer and the keyboard paths
/// that dismiss it early, and the accent colour the severity chooses.
/// </summary>
/// <remarks>
/// The countdown is a timer, driven by hand (<see cref="TestApplicationBackend"/>): the assertion is
/// about when the toast starts and stops its clock, not about how long a test waited.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class ToastTests : HeadlessTestBase
{
    [TestMethod]
    public void AToastDismissesItselfAfterItsDuration()
    {
        var backend = new TestApplicationBackend();
        var toast = new Toast("Saved") { Duration = TimeSpan.FromSeconds(2) };

        using var scene = HeadlessScene.Create(backend, toast, 320f, 80f);

        int dismissed = 0;
        toast.AddDismissedHandler(_ => dismissed++);

        scene.RunWhileShown(() =>
        {
            toast.Show();

            Assert.IsTrue(toast.IsOpen);
            Assert.IsTrue(backend.Timer.IsRunning, "an open toast is counting down");
            Assert.AreEqual(TimeSpan.FromSeconds(2), backend.Timer.Interval, "for the duration it was given");

            backend.Timer.Fire();
            backend.Drain();

            Assert.IsFalse(toast.IsOpen, "the message has gone");
            Assert.IsFalse(backend.Timer.IsRunning, "and so has its clock");
            Assert.AreEqual(1, dismissed, "the dismissed handler ran once");
        });
    }

    [TestMethod]
    public void AToastWithNoDurationWaitsToBeDismissed()
    {
        var backend = new TestApplicationBackend();
        var toast = new Toast("Notice") { Duration = null };

        using var scene = HeadlessScene.Create(backend, toast, 320f, 80f);

        scene.RunWhileShown(() =>
        {
            toast.Show();

            Assert.IsTrue(toast.IsOpen);
            Assert.IsFalse(backend.Timer.IsRunning, "there is no countdown to run");

            toast.Dismiss();

            Assert.IsFalse(toast.IsOpen);
        });
    }

    [TestMethod]
    public void AToastOnAnUnshownWindowRunsNoTimer()
    {
        var backend = new TestApplicationBackend();
        var toast = new Toast("Saved");

        using var scene = HeadlessScene.Create(backend, toast, 320f, 80f);

        toast.Show();

        Assert.IsTrue(toast.IsOpen, "the message is up as far as the tree is concerned");
        Assert.IsFalse(backend.Timer.IsRunning, "but nothing can see it, so nothing counts down");
    }

    [TestMethod]
    public void TheCloseGlyphDismissesAndTheMessageDoesNot()
    {
        var backend = new TestApplicationBackend();
        var toast = new Toast("Saved") { Duration = null };

        using var scene = HeadlessScene.Create(backend, toast, 320f, 80f);

        scene.RunWhileShown(() =>
        {
            toast.Show();

            scene.ClickAt(new SKPoint(20f, toast.Height / 2f));
            Assert.IsTrue(toast.IsOpen, "a press on the message itself changes nothing");

            scene.ClickAt(new SKPoint(toast.Width - 20f, toast.Height / 2f));
            Assert.IsFalse(toast.IsOpen, "a press on the close glyph dismisses it");
        });
    }

    [TestMethod]
    public void APressThatSlidesOffTheCloseGlyphDoesNotDismiss()
    {
        var backend = new TestApplicationBackend();
        var toast = new Toast("Saved") { Duration = null };

        using var scene = HeadlessScene.Create(backend, toast, 320f, 80f);

        scene.RunWhileShown(() =>
        {
            toast.Show();

            scene.Host.ProcessMouseMove(new SKPoint(toast.Width - 20f, toast.Height / 2f));
            scene.Host.ProcessMouseDown(
                new SKPoint(toast.Width - 20f, toast.Height / 2f),
                MouseButtons.Left,
                Modifiers.None);
            scene.Host.ProcessMouseUp(new SKPoint(20f, toast.Height / 2f), MouseButtons.Left, Modifiers.None);

            Assert.IsTrue(toast.IsOpen, "the press left the glyph before it was released");
        });
    }

    [TestMethod]
    public void EscapeDismissesAFocusedToast()
    {
        var backend = new TestApplicationBackend();
        var toast = new Toast("Saved") { Duration = null };

        using var scene = HeadlessScene.Create(backend, toast, 320f, 80f);

        scene.RunWhileShown(() =>
        {
            toast.Show();
            toast.Focus();

            scene.TypeKey(Key.Escape);

            Assert.IsFalse(toast.IsOpen, "a message the keyboard is on can be dismissed from it");
        });
    }

    [TestMethod]
    public void TheSeverityChoosesTheAccentBarColourAndTheBarCanBeOverridden()
    {
        var toast = new Toast("Saved") { Dock = DockStyle.Fill };

        using var scene = HeadlessScene.Create(toast, 320f, 60f);

        toast.Show();

        var info = scene.PixelAt(12, 30);

        toast.Severity = ToastSeverity.Error;
        var error = scene.PixelAt(12, 30);

        Assert.AreNotEqual(info, error, "an error does not look like a plain notice");

        toast.BarColor = new SKColor(0x12, 0x34, 0x56);
        Assert.AreEqual(new SKColor(0x12, 0x34, 0x56), scene.PixelAt(12, 30), "an application can brand its own");
    }

    [TestMethod]
    public void ShowingAToastDockedToAnEdgeMakesRoomForIt()
    {
        var content = new Label("Content") { Dock = DockStyle.Fill, AutoSize = false };
        var toast = new Toast("Saved") { Dock = DockStyle.Bottom, Duration = null };

        var card = new Panel
        {
            Dock = DockStyle.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = Thickness.Empty,
        };

        card.Controls.Add(content);
        card.Controls.Add(toast);

        using var scene = HeadlessScene.Create(card, 320f, 220f);

        float room = content.Height;
        Assert.IsFalse(toast.Visible, "a toast nobody has shown takes no room");

        toast.Show();
        Assert.IsTrue(content.Height < room, "the page makes room for the message");
        Assert.AreEqual(content.Bounds.Bottom, toast.Top, "which is docked under it");

        toast.Dismiss();
        Assert.AreEqual(room, content.Height, "and the room comes back when it goes");
    }

    [TestMethod]
    public void TheProgressFollowsShowAndDismissWhicheverWayItIsDriven()
    {
        using var toast = new Toast("Saved");

        Assert.AreEqual(0d, toast.ShowProgress, 0.000001d, "a message that was never shown has not come in");

        toast.Show();

        Assert.AreEqual(1d, toast.ShowProgress, 0.000001d);
        Assert.AreEqual(1f, toast.Opacity, 0.000001f, "and the message is drawn from it, so it is opaque");

        toast.Dismiss();

        Assert.AreEqual(0d, toast.ShowProgress, 0.000001d);
        Assert.AreEqual(0f, toast.Opacity, 0.000001f);
        Assert.IsFalse(toast.Visible, "reaching the bottom of the range is what takes it off the screen");

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => toast.ShowProgress = double.NaN);
    }

    [TestMethod]
    public void AHandlerThatTakesTheDismissalOverKeepsTheMessageUpUntilItSaysOtherwise()
    {
        using var toast = new Toast("Saved");
        using var scene = HeadlessScene.Create(toast, 320f, 80f);

        int dismissed = 0;
        toast.AddDismissedHandler(_ => dismissed++);

        toast.AddDismissingHandler(sender =>
        {
            // The contract the feature package relies on: assigning the value the progress already
            // has is what counts as taking the transition over, so the control does not snap and
            // does not remove the message.
            var control = (Toast)sender;
            control.ShowProgress = control.ShowProgress;
        });

        toast.Show();
        toast.Dismiss();

        Assert.IsFalse(toast.IsOpen, "the dismissal has been asked for");
        Assert.IsTrue(toast.Visible, "but the message is still on the screen");
        Assert.AreEqual(1d, toast.ShowProgress, 0.000001d);
        Assert.AreEqual(0, dismissed);
        Assert.IsFalse(toast.TabStop, "and it has already left the Tab order on its way out");
        Assert.IsFalse(toast.Focusable);

        toast.ShowProgress = 0d;

        Assert.IsFalse(toast.Visible, "bringing the progress down is what removes it");
        Assert.AreEqual(1, dismissed, "once");

        toast.ShowProgress = 0d;
        Assert.AreEqual(1, dismissed, "and settling on 0 a second time does not remove it again");
    }

    [TestMethod]
    public void TheToastMeasuresItsMessagePlusItsChrome()
    {
        using var toast = new Toast("Saved");
        using var longer = new Toast("Saved with a considerably longer message than the other one");

        var room = new SKSize(800f, 400f);

        Assert.IsTrue(longer.Measure(room).Width > toast.Measure(room).Width, "a longer message is a wider toast");
        Assert.IsTrue(toast.Measure(room).Height >= toast.MinimumSize.Height, "and the minimum height applies");
    }
}

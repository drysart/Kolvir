using SkiaSharp;
using XPlatUI;

namespace XPlatUI.Tests;

/// <summary>
/// The two halves of "the application is busy": the spinner that shows it and the layer that stops
/// the user from driving the window while it runs.
/// </summary>
/// <remarks>
/// The spinner is the second control after the marquee progress bar whose value is time, so the
/// timer is driven by hand (<see cref="TestApplicationBackend"/>): nothing sleeps and the assertion
/// is about when the control starts and stops its own clock, not about how long a test waited.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class BusyOverlayTests : HeadlessTestBase
{
    [TestMethod]
    public void ASpinnerInAWindowNobodyShowedRunsNoTimer()
    {
        var backend = new TestApplicationBackend();
        var spinner = new XPlatBusyIndicator { Dock = DockStyle.Fill };

        using var scene = HeadlessScene.Create(backend, spinner, 120f, 80f);

        // A harness paints a window it never showed; that is a frame, not the user seeing it.
        scene.Paint();

        Assert.IsFalse(backend.Timer.IsRunning, "nothing on screen has nothing to animate");
    }

    [TestMethod]
    public void ShowingTheWindowStartsTheSweepAndClearingBusyStopsIt()
    {
        var backend = new TestApplicationBackend();
        var spinner = new XPlatBusyIndicator { Dock = DockStyle.Fill };

        using var scene = HeadlessScene.Create(backend, spinner, 120f, 80f);

        scene.RunWhileShown(() =>
        {
            Assert.IsTrue(backend.Timer.IsRunning, "a spinner on screen should be going round");

            spinner.IsBusy = false;
            Assert.IsFalse(backend.Timer.IsRunning, "nothing is busy, so nothing has to animate");

            spinner.IsBusy = true;
            Assert.IsTrue(backend.Timer.IsRunning, "and it starts again when the work does");

            spinner.Visible = false;
            Assert.IsFalse(backend.Timer.IsRunning, "a hidden spinner has nothing to draw");
        });
    }

    [TestMethod]
    public void HidingAContainerStopsTheSpinnerInsideIt()
    {
        var backend = new TestApplicationBackend();
        var spinner = new XPlatBusyIndicator { Dock = DockStyle.Fill };
        var panel = new XPlatPanel
        {
            Dock = DockStyle.Fill,
            ShowFrame = false,
            IsCard = false,
            BorderWidth = 0f,
            Padding = XPlatThickness.Empty,
        };
        panel.Controls.Add(spinner);

        using var scene = HeadlessScene.Create(backend, panel, 120f, 80f);

        scene.RunWhileShown(() =>
        {
            Assert.IsTrue(backend.Timer.IsRunning);

            panel.Visible = false;
            Assert.IsFalse(backend.Timer.IsRunning, "an invisible container hides the spinner");

            panel.Visible = true;
            Assert.IsTrue(backend.Timer.IsRunning, "and it sweeps again once the container is back");
        });
    }

    [TestMethod]
    public void TheSpinnerTakesNoInput()
    {
        var spinner = new XPlatBusyIndicator { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(spinner, 120f, 80f);

        scene.Click(spinner);

        Assert.IsFalse(spinner.Focusable);
        Assert.IsFalse(spinner.TabStop);
        Assert.IsNull(scene.Host.FocusedControl, "a readout does not take the focus");
    }

    [TestMethod]
    public void AnAutoSizedSpinnerIsItsMinimumSize()
    {
        using var spinner = new XPlatBusyIndicator();

        Assert.AreEqual(new SKSize(16f, 16f), spinner.Measure(new SKSize(400f, 400f)));
        Assert.AreEqual(new SKSize(32f, 32f), spinner.Size, "and is created at the size it is usually drawn");
    }

    [TestMethod]
    public void TheOverlayTakesThePressMeantForTheControlUnderIt()
    {
        // Two Fill children of the same panel cover the same area, which is how a layer sits over
        // the page. A layer added beside a child docked to an edge would only get what is left.
        var content = Muted();
        var button = new XPlatButton("Save") { Dock = DockStyle.Top, AutoSize = true };
        content.Controls.Add(button);

        var root = Muted();
        root.Controls.Add(content);

        int clicks = 0;
        button.AddClickHandler(_ => clicks++);

        using var scene = HeadlessScene.Create(root, 300f, 200f);

        var overlay = new XPlatOverlay();
        root.Controls.Add(overlay);

        scene.Click(button);
        Assert.AreEqual(0, clicks, "the layer is above the button and takes the press");

        overlay.Visible = false;
        scene.Click(button);
        Assert.AreEqual(1, clicks, "and hiding it gives the button back");
    }

    [TestMethod]
    public void TheOverlaySwallowsTheKeyboardAndGivesItBack()
    {
        var root = Muted();
        var box = new XPlatTextBox { Dock = DockStyle.Top, Text = string.Empty };
        root.Controls.Add(box);

        using var scene = HeadlessScene.Create(root, 300f, 200f);

        box.Focus();

        scene.RunWhileShown(() =>
        {
            scene.Type("ab");
            Assert.AreEqual("ab", box.Text, "the field works before the overlay arrives");

            var overlay = new XPlatOverlay { Dock = DockStyle.Fill };
            root.Controls.Add(overlay);

            scene.Type("cd");
            scene.Key(XPlatKey.Backspace);

            Assert.AreEqual("ab", box.Text, "nothing typed reaches the field while the layer is up");
            Assert.IsTrue(
                scene.Host.ProcessKeyDown(XPlatKey.Tab),
                "nor does Tab move the focus out from under it");

            overlay.Visible = false;
            scene.Type("ef");

            Assert.AreEqual("abef", box.Text, "and hiding the layer gives the keyboard back");
        });
    }

    [TestMethod]
    public void ACentredChildSitsInTheMiddleOfTheLayer()
    {
        var overlay = new XPlatOverlay();
        var spinner = new XPlatBusyIndicator();
        overlay.Controls.Add(spinner);

        using var scene = HeadlessScene.Create(overlay, 300f, 200f);

        Assert.AreEqual(32f, spinner.Width);
        Assert.AreEqual(150f, spinner.Left + (spinner.Width / 2f), "centred across the layer");
        Assert.AreEqual(100f, spinner.Top + (spinner.Height / 2f), "and down it");
    }

    [TestMethod]
    public void ADockedChildKeepsItsEdge()
    {
        var overlay = new XPlatOverlay { Dock = DockStyle.Fill };
        var message = new XPlatLabel("Working") { Dock = DockStyle.Top, AutoSize = false, Height = 24f };
        var spinner = new XPlatBusyIndicator();

        overlay.Controls.Add(message);
        overlay.Controls.Add(spinner);

        using var scene = HeadlessScene.Create(overlay, 300f, 200f);

        Assert.AreEqual(0f, message.Top, "a docked child stays at its edge");
        Assert.AreEqual(300f, message.Width);
        Assert.AreEqual(
            24f + ((200f - 24f) / 2f),
            spinner.Top + (spinner.Height / 2f),
            "and the free child is centred in what the docked one left");
    }

    [TestMethod]
    public void TheDimmingCoversWhatIsUnderItWithoutHidingIt()
    {
        var root = Muted();
        root.BackColor = new SKColor(0xC0, 0x20, 0x20);

        using var scene = HeadlessScene.Create(root, 200f, 120f);

        var overlay = new XPlatOverlay { Dock = DockStyle.Fill, Dim = 0f };
        root.Controls.Add(overlay);

        Assert.AreEqual(new SKColor(0xC0, 0x20, 0x20), scene.PixelAt(20, 20), "no dimming shows the surface");

        overlay.Dim = 1f;
        Assert.AreEqual(XPlatTheme.Background, scene.PixelAt(20, 20), "full dimming is the window colour");

        overlay.Dim = 0.5f;
        var half = scene.PixelAt(20, 20);

        Assert.AreNotEqual(new SKColor(0xC0, 0x20, 0x20), half, "and half is neither extreme");
        Assert.AreNotEqual(XPlatTheme.Background, half);
    }

    private static XPlatPanel Muted() => new()
    {
        Dock = DockStyle.Fill,
        ShowFrame = false,
        IsCard = false,
        BackColor = null,
        BorderWidth = 0f,
        CornerRadius = 0f,
        Padding = XPlatThickness.Empty,
    };
}

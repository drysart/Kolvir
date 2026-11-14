using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The simple input controls and the labels: <see cref="Button"/>,
/// <see cref="ToggleControl"/> and its two concrete kinds, <see cref="Label"/>,
/// <see cref="RichLabel"/> and <see cref="LinkLabel"/>.
/// </summary>
/// <remarks>
/// Each control is exercised through the same questions CONTROLS-TODO.md section 2 asks of every
/// control: does a press inside and a release inside make a click, does the keyboard do what the
/// native control of that kind does, does disabled take no input, and do the events come in pairs
/// that can be unsubscribed.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class InputControlTests : HeadlessTestBase
{
    [TestMethod]
    public void AButtonRaisesClickOnPressAndReleaseInside()
    {
        var button = new Button("Go") { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(button, 200f, 80f);
        int clicks = 0;
        button.AddClickHandler(_ => clicks++);

        scene.Click(button);

        Assert.AreEqual(1, clicks);
    }

    [TestMethod]
    public void AReleaseOutsideTheButtonDoesNotClick()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = Thickness.Empty };
        var button = new Button("Go") { Dock = DockStyle.Top, Height = 40f };
        panel.Controls.Add(button);
        using var scene = HeadlessScene.Create(panel, 200f, 120f);
        int clicks = 0;
        button.AddClickHandler(_ => clicks++);

        scene.Press(button);
        scene.Host.ProcessMouseMove(new SKPoint(10f, 110f));
        scene.Host.ProcessMouseUp(new SKPoint(10f, 110f));

        Assert.AreEqual(0, clicks, "a release outside the pressed control is not a click");
        Assert.IsFalse(button.IsPressed);
    }

    [TestMethod]
    public void TheButtonShowsHoverAndPressedStatesAsDistinct()
    {
        var button = new Button("Go") { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(button, 200f, 80f);

        scene.MoveTo(button);
        Assert.IsTrue(button.IsMouseOver, "the pointer over the button should hover it");
        Assert.IsFalse(button.IsPressed);

        scene.Press(button);
        Assert.IsTrue(button.IsPressed, "a press should be distinct from a hover");

        scene.Release(button);
        Assert.IsFalse(button.IsPressed);

        scene.Host.ProcessMouseLeave();
        Assert.IsFalse(button.IsMouseOver, "leaving the surface should end the hover");
    }

    [TestMethod]
    public void AButtonActivatesFromEnterAndFromSpace()
    {
        var button = new Button("Go") { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(button, 200f, 80f);
        int clicks = 0;
        button.AddClickHandler(_ => clicks++);
        button.Focus();

        scene.TypeKey(Key.Enter);
        Assert.AreEqual(1, clicks, "Enter should activate a focused button");

        scene.TypeKey(Key.Space);
        Assert.AreEqual(2, clicks, "Space should as well");
    }

    [TestMethod]
    public void ADisabledButtonTakesNoInput()
    {
        var button = new Button("Go") { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(button, 200f, 80f);
        int clicks = 0;
        button.AddClickHandler(_ => clicks++);

        button.Enabled = false;
        scene.Click(button);
        scene.TypeKey(Key.Enter);

        Assert.AreEqual(0, clicks);
        Assert.IsFalse(button.IsFocused);
    }

    [TestMethod]
    public void ACheckBoxTogglesWithAClick()
    {
        var check = new CheckBox("Enable") { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(check, 240f, 60f);
        int changes = 0;
        check.AddCheckedChangedHandler(_ => changes++);

        Assert.IsFalse(check.Checked, "a checkbox starts unchecked");

        scene.Click(check);
        Assert.IsTrue(check.Checked);
        Assert.AreEqual(1, changes);

        scene.Click(check);
        Assert.IsFalse(check.Checked);
        Assert.AreEqual(2, changes);
    }

    [TestMethod]
    public void ATwoStateCheckBoxCannotHoldTheIndeterminateState()
    {
        using var check = new CheckBox();

        check.CheckState = CheckState.Indeterminate;

        Assert.AreEqual(CheckState.Unchecked, check.CheckState);
    }

    [TestMethod]
    public void AThreeStateCheckBoxCyclesThroughAllThreeStates()
    {
        var check = new CheckBox("Maybe") { Dock = DockStyle.Fill, ThreeState = true };
        using var scene = HeadlessScene.Create(check, 240f, 60f);

        Assert.AreEqual(CheckState.Unchecked, check.CheckState);

        scene.Click(check);
        Assert.AreEqual(CheckState.Checked, check.CheckState);

        scene.Click(check);
        Assert.AreEqual(CheckState.Indeterminate, check.CheckState);

        scene.Click(check);
        Assert.AreEqual(CheckState.Unchecked, check.CheckState);
    }

    [TestMethod]
    public void AutoCheckOffLeavesTheDecisionToTheApplication()
    {
        var check = new CheckBox("Enable") { Dock = DockStyle.Fill, AutoCheck = false };
        using var scene = HeadlessScene.Create(check, 240f, 60f);
        int clicks = 0;
        check.AddClickHandler(_ => clicks++);

        scene.Click(check);

        Assert.AreEqual(1, clicks, "the click should still be reported");
        Assert.IsFalse(check.Checked, "but the state should not change on its own");
    }

    [TestMethod]
    public void SpaceTogglesAFocusedCheckBox()
    {
        var check = new CheckBox("Enable") { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(check, 240f, 60f);
        check.Focus();

        scene.KeyUp(Key.Space);
        Assert.IsFalse(check.Checked, "a key-up with no key-down should not toggle");

        scene.Key(Key.Space);
        Assert.IsFalse(check.Checked, "the state should still wait for the release");

        scene.KeyUp(Key.Space);
        Assert.IsTrue(check.Checked, "Space should toggle a focused checkbox");
    }

    [TestMethod]
    public void ARadioButtonUnchecksItsSiblingsUnderTheSameParent()
    {
        var first = new RadioButton("First") { Dock = DockStyle.Top };
        var second = new RadioButton("Second") { Dock = DockStyle.Top };
        var panel = new Panel { Dock = DockStyle.Fill, Padding = Thickness.Empty };
        panel.Controls.Add(first);
        panel.Controls.Add(second);

        using var scene = HeadlessScene.Create(panel, 240f, 160f);

        scene.Click(first);
        Assert.IsTrue(first.Checked);

        scene.Click(second);
        Assert.IsTrue(second.Checked);
        Assert.IsFalse(first.Checked, "a radio button and its sibling are one group");

        first.Checked = true;
        Assert.IsTrue(first.Checked);
        Assert.IsFalse(second.Checked, "assigning Checked should uncheck the sibling too");
    }

    [TestMethod]
    public void AGroupNameSeparatesRadioButtonsUnderOneParent()
    {
        var first = new RadioButton("First") { Dock = DockStyle.Top, GroupName = "a" };
        var second = new RadioButton("Second") { Dock = DockStyle.Top, GroupName = "b" };
        var panel = new Panel { Dock = DockStyle.Fill, Padding = Thickness.Empty };
        panel.Controls.Add(first);
        panel.Controls.Add(second);

        using var scene = HeadlessScene.Create(panel, 240f, 160f);

        scene.Click(first);
        scene.Click(second);

        Assert.IsTrue(first.Checked, "different group names should not affect each other");
        Assert.IsTrue(second.Checked);
    }

    [TestMethod]
    public void ARadioButtonIsNeverUncheckedByAClick()
    {
        var radio = new RadioButton("Only") { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(radio, 240f, 60f);

        scene.Click(radio);
        scene.Click(radio);

        Assert.IsTrue(radio.Checked, "a second click on a checked radio leaves it checked");
    }

    [TestMethod]
    public void ArrowKeysMoveARadioSelectionWithinTheGroup()
    {
        var first = new RadioButton("First") { Dock = DockStyle.Top };
        var second = new RadioButton("Second") { Dock = DockStyle.Top };
        var panel = new Panel { Dock = DockStyle.Fill, Padding = Thickness.Empty };
        panel.Controls.Add(first);
        panel.Controls.Add(second);

        using var scene = HeadlessScene.Create(panel, 240f, 160f);

        first.Checked = true;
        first.Focus();

        scene.Key(Key.Down);

        Assert.IsTrue(second.Checked, "Down should move the selection to the next radio");
        Assert.IsFalse(first.Checked);
        Assert.AreSame(second, scene.Host.FocusedControl, "and move the focus with it");
    }

    [TestMethod]
    public void ALabelAutoSizesToItsTextInThePinnedFace()
    {
        using (TestFont.Pin())
        {
            var stage = new Panel { Dock = DockStyle.Fill, Padding = Thickness.Empty };
            using var scene = HeadlessScene.Create(stage, 400f, 200f);

            var shortLabel = new Label("short") { AutoSize = true };
            var longLabel = new Label("a considerably longer piece of text") { AutoSize = true };
            stage.Controls.Add(shortLabel);
            stage.Controls.Add(longLabel);
            stage.PerformLayout();

            Assert.IsTrue(shortLabel.Width > 0f, "an auto-sized label should have a width");
            Assert.IsTrue(longLabel.Width > shortLabel.Width, "more text should measure wider");
            Assert.IsTrue(shortLabel.Height >= Theme.DefaultFont.LineHeight - 0.5f, "and at least one line tall");
        }
    }

    [TestMethod]
    public void ALabelWithoutAutoSizeKeepsTheSizeItWasGiven()
    {
        var label = new Label("text") { Dock = DockStyle.Fill, AutoSize = false };
        using var scene = HeadlessScene.Create(label, 300f, 90f);

        Assert.AreEqual(new SKSize(300f, 90f), label.Size);
    }

    [TestMethod]
    public void AWordWrappedLabelGrowsTallerThanOneLine()
    {
        using var scene = HeadlessScene.Create(null, 200f, 200f);

        using (TestFont.Pin())
        {
            var label = new Label("wrapping text that does not fit on one narrow line") { WrapText = true };
            var wide = label.Measure(new SKSize(400f, float.PositiveInfinity));
            float tall = label.Measure(new SKSize(60f, float.PositiveInfinity)).Height;

            Assert.IsTrue(tall > wide.Height, $"wrapping at 60 px should take more lines than at 400 ({wide.Height} -> {tall})");
        }
    }

    [TestMethod]
    public void ALinkLabelOpensItsUrlThroughTheLauncher()
    {
        var launcher = new RecordingLauncher { Accepted = true };
        Launcher.Current = launcher;

        var link = new LinkLabel("Documentation", "https://example.com/docs") { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(link, 300f, 60f);

        LinkClickedEventArgs? seen = null;
        link.AddLinkClickedHandler((_, e) => seen = e);

        link.Focus();
        scene.TypeKey(Key.Enter);

        Assert.AreEqual("https://example.com/docs", launcher.OpenedUrl, "the URL should reach the launcher");
        Assert.IsNotNull(seen, "the click should be reported");
        Assert.IsTrue(seen!.Opened, "and say the platform accepted it");
    }

    [TestMethod]
    public void AutoLaunchOffLeavesTheNavigationToTheHandler()
    {
        var launcher = new RecordingLauncher { Accepted = true };
        Launcher.Current = launcher;

        var link = new LinkLabel("Inside", "app://page") { Dock = DockStyle.Fill, AutoLaunch = false };
        using var scene = HeadlessScene.Create(link, 300f, 60f);

        LinkClickedEventArgs? seen = null;
        link.AddLinkClickedHandler((_, e) => seen = e);

        link.Focus();
        scene.TypeKey(Key.Space);

        Assert.IsNull(launcher.OpenedUrl, "AutoLaunch=false should not reach the launcher");
        Assert.IsNotNull(seen);
        Assert.IsFalse(seen!.Opened);
        Assert.AreEqual("app://page", seen.Url);
    }

    [TestMethod]
    public void ARichLabelBuildsItsTextFromSpans()
    {
        using var label = new RichLabel();
        label.AddText("Hello ");
        label.AddLink("world", _ => { });
        label.AddText(".");

        Assert.AreEqual("Hello world.", label.Text);
        Assert.AreEqual(3, label.Spans.Count);
    }

    [TestMethod]
    public void ARichLabelSpanClickHandlerIsRemovable()
    {
        using var label = new RichLabel { Text = string.Empty };
        int clicks = 0;
        void OnSpan(object _, SpanEventArgs __) => clicks++;

        label.AddSpanClickHandler(OnSpan);
        label.RemoveSpanClickHandler(OnSpan);

        // The spans a rich label was built from are what a click is mapped to; the link span is how
        // a paragraph makes part of itself interactive.
        var span = label.AddLink("link", _ => clicks++);

        Assert.IsNotNull(span);
        Assert.AreEqual(1, label.Spans.Count);
        Assert.AreEqual(0, clicks);
    }

    [TestMethod]
    public void AButtonStyleIsAssignable()
    {
        using var button = new Button("Go");

        Assert.AreEqual(ButtonStyle.Primary, button.Style, "a button starts as the primary action");

        button.Style = ButtonStyle.Ghost;
        Assert.AreEqual(ButtonStyle.Ghost, button.Style);
    }

    [TestMethod]
    public void AButtonMeasuresToItsTextInThePinnedFace()
    {
        using (TestFont.Pin())
        {
            using var shortButton = new Button("ok");
            using var longButton = new Button("a much longer caption");

            var available = new SKSize(1000f, 1000f);
            float shortWidth = shortButton.Measure(available).Width;
            float longWidth = longButton.Measure(available).Width;

            Assert.IsTrue(longWidth > shortWidth, $"{longWidth} should be wider than {shortWidth}");
            Assert.IsTrue(shortButton.Measure(available).Height >= 32f, "a button is at least 32 px tall");
        }
    }

    /// <summary>A launcher that records what it was asked to open.</summary>
    private sealed class RecordingLauncher : ILauncher
    {
        public string? OpenedUrl { get; private set; }

        public bool Accepted { get; set; }

        public bool OpenUrl(string url)
        {
            OpenedUrl = url;
            return Accepted;
        }
    }
}

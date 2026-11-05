using SkiaSharp;
using XPlatUI;

namespace XPlatUI.Tests;

/// <summary>
/// The small widgets and the popup-based controls whose behaviour is observable without opening a
/// real popup: progress bar, track bar, group box, scroll bar, tab control, combo box, date picker,
/// tool tip, image and the strip.
/// </summary>
/// <remarks>
/// Where a control's value is interaction - the combo box's drop-down, the date picker's calendar,
/// the tool tip's delayed popup - this file asserts the state the control reports and leaves the
/// popup window itself to the backend tests. That keeps the suite independent of whether a popup can
/// be created on a runner with no window server.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class WidgetTests : HeadlessTestBase
{
    [TestMethod]
    public void AProgressBarStartsEmptyAndClampsItsValue()
    {
        using var bar = new XPlatProgressBar { Maximum = 200f };

        Assert.AreEqual(0f, bar.Minimum);
        Assert.AreEqual(0f, bar.Value);
        Assert.AreEqual(0f, bar.Percent);

        bar.Value = 500f;
        Assert.AreEqual(200f, bar.Value, "a value above the maximum is clamped");
        Assert.AreEqual(1f, bar.Percent, "percent is the fraction of the range, not a 0-100 number");

        bar.Value = -5f;
        Assert.AreEqual(0f, bar.Value, "and one below the minimum too");
    }

    [TestMethod]
    public void PerformStepAndIncrementMoveTheProgressBar()
    {
        using var bar = new XPlatProgressBar { Step = 10f };

        bar.PerformStep();
        Assert.AreEqual(10f, bar.Value);

        bar.Increment(15f);
        Assert.AreEqual(25f, bar.Value);
        Assert.AreEqual(0.25f, bar.Percent, "percent is the fraction of the range");
    }

    [TestMethod]
    public void AMarqueeBarInAWindowThatWasNeverShownRunsNoTimer()
    {
        var backend = new TestApplicationBackend();
        var bar = new XPlatProgressBar { Dock = DockStyle.Fill, Style = XPlatProgressBarStyle.Marquee };

        using var scene = HeadlessScene.Create(backend, bar, 240f, 40f);

        // A harness paints a window it never showed; that is a frame, not the user seeing it.
        scene.Paint();

        Assert.IsFalse(backend.Timer.IsRunning, "a bar in a window nobody has shown has nothing to animate");
    }

    [TestMethod]
    public void ShowingTheWindowStartsTheMarqueeTimerAndHidingTheBarStopsIt()
    {
        var backend = new TestApplicationBackend();
        var bar = new XPlatProgressBar
        {
            Dock = DockStyle.Fill,
            Style = XPlatProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = TimeSpan.FromMilliseconds(40),
        };

        using var scene = HeadlessScene.Create(backend, bar, 240f, 40f);

        WhenShown(scene, () =>
        {
            Assert.IsTrue(backend.Timer.IsRunning, "a bar on screen should be sweeping");
            Assert.AreEqual(
                TimeSpan.FromMilliseconds(40),
                backend.Timer.Interval,
                "at the speed the application asked for");

            bar.Visible = false;

            Assert.IsFalse(backend.Timer.IsRunning, "a hidden bar has nothing to animate");

            bar.Visible = true;

            Assert.IsTrue(backend.Timer.IsRunning, "and showing it starts the sweep again");
        });
    }

    [TestMethod]
    public void HidingAContainerStopsTheMarqueeTimerOfTheBarInsideIt()
    {
        var backend = new TestApplicationBackend();
        var bar = new XPlatProgressBar { Dock = DockStyle.Fill, Style = XPlatProgressBarStyle.Marquee };
        var panel = new XPlatPanel { Dock = DockStyle.Fill, Padding = XPlatThickness.Empty, BorderWidth = 0f };
        panel.Controls.Add(bar);

        using var scene = HeadlessScene.Create(backend, panel, 240f, 60f);
        scene.Paint();

        WhenShown(scene, () =>
        {
            Assert.IsTrue(backend.Timer.IsRunning);

            panel.Visible = false;

            Assert.IsFalse(backend.Timer.IsRunning, "an invisible container hides the bar, so its clock stops");

            panel.Visible = true;

            Assert.IsTrue(backend.Timer.IsRunning, "and the bar sweeps again once the container is back");
        });
    }

    [TestMethod]
    public void ClosingTheWindowStopsTheMarqueeTimer()
    {
        var backend = new TestApplicationBackend();
        var bar = new XPlatProgressBar { Dock = DockStyle.Fill, Style = XPlatProgressBarStyle.Marquee };

        using var scene = HeadlessScene.Create(backend, bar, 240f, 40f);
        scene.Paint();

        WhenShown(scene, () =>
        {
            Assert.IsTrue(backend.Timer.IsRunning);

            scene.Window.Close();

            Assert.IsFalse(backend.Timer.IsRunning, "a closed window has nothing left to animate");
        });
    }

    [TestMethod]
    public void AMarqueeBarAddedToAShownWindowStartsAndStopsWhenItIsRemoved()
    {
        var backend = new TestApplicationBackend();
        var bar = new XPlatProgressBar { Dock = DockStyle.Fill, Style = XPlatProgressBarStyle.Marquee };
        var host = new XPlatPanel { Dock = DockStyle.Fill, Padding = XPlatThickness.Empty, BorderWidth = 0f };

        using var scene = HeadlessScene.Create(backend, host, 240f, 60f);
        scene.Paint();

        WhenShown(scene, () =>
        {
            Assert.IsFalse(backend.Timer.IsRunning, "the bar is not in the tree yet");

            host.Controls.Add(bar);

            Assert.IsTrue(backend.Timer.IsRunning, "adding it to a window on screen starts the sweep");

            host.Controls.Remove(bar);

            Assert.IsFalse(backend.Timer.IsRunning, "and taking it out of the window stops it");
        });
    }

    [TestMethod]
    public void TogglingTheMarqueeStyleWhileTheWindowIsNotShownLeavesNoTimerRunning()
    {
        var backend = new TestApplicationBackend();
        var bar = new XPlatProgressBar { Dock = DockStyle.Fill, Style = XPlatProgressBarStyle.Marquee };

        using var scene = HeadlessScene.Create(backend, bar, 240f, 40f);

        scene.Paint();
        bar.Style = XPlatProgressBarStyle.Continuous;
        bar.Style = XPlatProgressBarStyle.Marquee;
        scene.Paint();

        Assert.IsFalse(backend.Timer.IsRunning, "nothing about a window nobody showed makes the bar animate");
    }

    [TestMethod]
    public void AResumedMarqueeKeepsItsPhaseAndReusesItsOneTimer()
    {
        var backend = new TestApplicationBackend();
        var bar = new XPlatProgressBar
        {
            Dock = DockStyle.Fill,
            Style = XPlatProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = TimeSpan.FromSeconds(1),
        };

        using var scene = HeadlessScene.Create(backend, bar, 240f, 40f);

        WhenShown(scene, () =>
        {
            // The first tick after a start advances by exactly one interval, so the phase after it
            // is a known fraction of the sweep rather than anything the wall clock decided.
            backend.Timer.Fire();
            backend.Drain();

            using var afterOneTick = scene.Render();

            for (int i = 0; i < 3; i++)
            {
                bar.Visible = false;
                bar.Visible = true;
            }

            Assert.AreEqual(1, backend.Timer.Attachments, "resuming reuses the one timer the bar built");
            Assert.IsTrue(backend.Timer.IsRunning);

            backend.Timer.Fire();
            backend.Drain();

            using var afterResume = scene.Render();

            CollectionAssert.AreNotEqual(
                afterOneTick.Bytes,
                afterResume.Bytes,
                "the sweep resumes from where it stopped instead of jumping back to its start");
        });
    }

    [TestMethod]
    public void ATrackBarClampsItsValueAndReportsEachChange()
    {
        using var bar = new XPlatTrackBar();
        int changes = 0;
        bar.AddValueChangedHandler(_ => changes++);

        Assert.AreEqual(0f, bar.Minimum);
        Assert.AreEqual(10f, bar.Maximum);

        bar.Value = 20f;
        Assert.AreEqual(10f, bar.Value, "a track bar clamps to its maximum");

        bar.Value = 10f;
        Assert.AreEqual(1, changes, "assigning the value it already holds raises nothing");

        bar.Value = 2f;
        Assert.AreEqual(2, changes);
    }

    [TestMethod]
    public void TheTrackBarKeysUseTheSmallAndLargeChanges()
    {
        using var bar = new XPlatTrackBar { Value = 5f, SmallChange = 1f, LargeChange = 5f };
        using var scene = HeadlessScene.Create(bar, 200f, 40f);
        bar.Focus();

        scene.Key(XPlatKey.Right);
        Assert.AreEqual(6f, bar.Value, "an arrow key moves by SmallChange");

        scene.Key(XPlatKey.PageDown);
        Assert.AreEqual(1f, bar.Value, "a page key moves by LargeChange");

        scene.Key(XPlatKey.End);
        Assert.AreEqual(10f, bar.Value);

        scene.Key(XPlatKey.Home);
        Assert.AreEqual(0f, bar.Value);
    }

    [TestMethod]
    public void ClickingTheTrackPagesTowardTheClick()
    {
        var bar = new XPlatTrackBar { Dock = DockStyle.Fill, Value = 5f, LargeChange = 2f };
        using var scene = HeadlessScene.Create(bar, 220f, 40f);

        var origin = bar.GetPositionInHost();
        var y = origin.Y + bar.Height / 2f;

        scene.ClickAt(new SKPoint(origin.X + (bar.Width * 0.8f), y));
        Assert.AreEqual(7f, bar.Value, "a click to the right of the knob pages forward by LargeChange");

        scene.ClickAt(new SKPoint(origin.X + (bar.Width * 0.2f), y));
        Assert.AreEqual(5f, bar.Value, "and one to the left pages back");
    }

    [TestMethod]
    public void AGroupBoxLaysItsContentOutBelowTheCaption()
    {
        var group = new XPlatGroupBox("Options") { Dock = DockStyle.Fill };
        var content = new XPlatPanel
        {
            Dock = DockStyle.Fill,
            Padding = XPlatThickness.Empty,
            BorderWidth = 0f,
            ShowFrame = false,
        };
        group.Controls.Add(content);

        using var scene = HeadlessScene.Create(group, 240f, 160f);

        Assert.IsTrue(content.Top > 0f, "the caption band should push the content down");
        Assert.IsTrue(content.Height > 0f);
        Assert.IsTrue(content.Height < group.Height, $"the content should not cover the caption ({content.Height} of {group.Height})");
    }

    [TestMethod]
    public void AScrollBarClampsItsValueAndReportsWhetherItCanScroll()
    {
        using var bar = new XPlatScrollBar { Minimum = 0f, Maximum = 100f };

        Assert.IsTrue(bar.IsScrollable, "a range with room in it scrolls");

        bar.Value = 500f;
        Assert.AreEqual(100f, bar.Value, "a value past the end is clamped");

        bar.Value = -20f;
        Assert.AreEqual(0f, bar.Value);

        bar.Maximum = 0f;
        Assert.IsFalse(bar.IsScrollable, "a bar with no range does not scroll");

        bar.Enabled = false;
        Assert.IsFalse(bar.IsScrollable, "nor does a disabled one");
    }

    [TestMethod]
    public void TheScrollBarKeysMoveByTheSmallAndLargeChange()
    {
        using var bar = new XPlatScrollBar
        {
            Orientation = XPlatOrientation.Horizontal,
            Minimum = 0f,
            Maximum = 100f,
            Value = 50f,
            SmallChange = 1f,
            LargeChange = 10f,
        };

        using var scene = HeadlessScene.Create(bar, 200f, 20f);
        bar.Focus();

        scene.Key(XPlatKey.Right);
        Assert.AreEqual(51f, bar.Value);

        scene.Key(XPlatKey.PageDown);
        Assert.AreEqual(61f, bar.Value);

        scene.Key(XPlatKey.End);
        Assert.AreEqual(100f, bar.Value);

        scene.Key(XPlatKey.Home);
        Assert.AreEqual(0f, bar.Value);
    }

    [TestMethod]
    public void OnlyTheSelectedTabPageIsVisible()
    {
        var tabs = new XPlatTabControl { Dock = DockStyle.Fill };
        var first = new XPlatTabPage("One");
        var second = new XPlatTabPage("Two");
        var third = new XPlatTabPage("Three");
        tabs.TabPages.Add(first);
        tabs.TabPages.Add(second);
        tabs.TabPages.Add(third);

        using var scene = HeadlessScene.Create(tabs, 320f, 200f);

        Assert.AreEqual(0, tabs.SelectedIndex);
        Assert.AreSame(first, tabs.SelectedTab);
        Assert.IsTrue(first.Visible, "the selected page is shown");
        Assert.IsFalse(second.Visible, "and the others are not");

        int changes = 0;
        tabs.AddSelectedIndexChangedHandler(_ => changes++);

        tabs.SelectedIndex = 1;

        Assert.AreEqual(1, changes);
        Assert.AreSame(second, tabs.SelectedTab);
        Assert.IsTrue(second.Visible);
        Assert.IsFalse(first.Visible);

        tabs.SelectedIndex = 1;
        Assert.AreEqual(1, changes, "selecting the page that is already selected raises nothing");
    }

    [TestMethod]
    public void ATabSelectionIsClampedToThePagesThatExist()
    {
        var tabs = new XPlatTabControl();
        tabs.TabPages.Add(new XPlatTabPage("One"));
        tabs.TabPages.Add(new XPlatTabPage("Two"));

        using var scene = HeadlessScene.Create(tabs, 320f, 200f);

        tabs.SelectedIndex = 99;
        Assert.AreEqual(1, tabs.SelectedIndex, "an index past the last page clamps to it");

        tabs.SelectedIndex = -5;
        Assert.AreEqual(0, tabs.SelectedIndex);
    }

    [TestMethod]
    public void AComboBoxReportsItsSelectionAndText()
    {
        var combo = new XPlatComboBox { Dock = DockStyle.Top };
        combo.Items.Add("alpha");
        combo.Items.Add("beta");
        combo.Items.Add("gamma");

        using var scene = HeadlessScene.Create(combo, 240f, 120f);

        Assert.AreEqual(3, combo.ItemCount);
        Assert.AreEqual(-1, combo.SelectedIndex, "nothing is selected until the user picks");
        Assert.AreEqual(string.Empty, combo.SelectedText);

        int changes = 0;
        combo.AddSelectedIndexChangedHandler(_ => changes++);

        combo.SelectedIndex = 2;

        Assert.AreEqual("gamma", combo.SelectedText);
        Assert.AreEqual("gamma", combo.Text);
        Assert.AreEqual("gamma", combo.SelectedItem);
        Assert.AreEqual(1, changes);

        combo.Text = string.Empty;
        Assert.AreEqual(-1, combo.SelectedIndex, "clearing the text clears the selection");
    }

    [TestMethod]
    public void TheDropDownListIsTheControlItems()
    {
        var combo = new XPlatComboBox();
        combo.Items.Add("one");

        Assert.AreEqual(1, combo.DropDownList.ItemCount);
        Assert.AreEqual(XPlatSelectionMode.One, combo.DropDownList.SelectionMode);
        Assert.IsFalse(combo.IsDropDownOpen, "a combo box starts closed");
    }

    [TestMethod]
    public void ADatePickerClampsItsValueToTheRange()
    {
        using var picker = new XPlatDateTimePicker
        {
            MinDate = new DateTime(2024, 1, 1),
            MaxDate = new DateTime(2024, 12, 31),
            Value = new DateTime(2024, 6, 15),
        };

        Assert.AreEqual(new DateTime(2024, 6, 15), picker.Value);

        picker.Value = new DateTime(2030, 1, 1);
        Assert.AreEqual(new DateTime(2024, 12, 31), picker.Value, "a later date clamps to MaxDate");

        picker.Value = new DateTime(2000, 1, 1);
        Assert.AreEqual(new DateTime(2024, 1, 1), picker.Value, "and an earlier one to MinDate");

        picker.Value = null;
        Assert.IsNull(picker.Value, "null is an empty field, not a clamp");
    }

    [TestMethod]
    public void TypedTextIsParsedOnEnter()
    {
        using var culture = CultureScope.English();

        var picker = new XPlatDateTimePicker
        {
            Format = XPlatDateTimePickerFormat.Short,
        };

        using var scene = HeadlessScene.Create(picker, 240f, 60f);
        picker.Focus();

        int changes = 0;
        picker.AddValueChangedHandler(_ => changes++);

        picker.SelectAll();
        scene.Type("3/4/2025");
        scene.TypeKey(XPlatKey.Enter);

        Assert.AreEqual(new DateTime(2025, 3, 4), picker.Value, "the typed date should be committed on Enter");
        Assert.AreEqual(1, changes, "and reported once");
    }

    [TestMethod]
    public void TheDateFormatChangesTheText()
    {
        using var picker = new XPlatDateTimePicker { Value = new DateTime(2025, 3, 4) };

        picker.Format = XPlatDateTimePickerFormat.Short;
        string shortText = picker.Text;

        picker.Format = XPlatDateTimePickerFormat.Long;
        string longText = picker.Text;

        Assert.AreNotEqual(shortText, longText, "the two formats should not produce the same text");
        Assert.IsTrue(shortText.Contains('3') && shortText.Contains('4'));
    }

    [TestMethod]
    public void AToolTipRemembersOneTextPerControlAndStartsHidden()
    {
        using var toolTip = new XPlatToolTip();
        using var first = new XPlatButton("First");
        using var second = new XPlatButton("Second");

        toolTip.SetToolTip(first, "The first button");

        Assert.AreEqual("The first button", toolTip.GetToolTip(first));
        Assert.IsNull(toolTip.GetToolTip(second), "a control with no text has no tool tip");
        Assert.IsTrue(toolTip.Active, "a tool tip is active by default");
        Assert.IsFalse(toolTip.IsVisible, "and shows nothing until the hover delay passes");

        toolTip.SetToolTip(first, null);
        Assert.IsNull(toolTip.GetToolTip(first), "assigning null removes the tool tip");

        toolTip.Hide();
        Assert.IsFalse(toolTip.IsVisible);
    }

    [TestMethod]
    public void AnImageLoadsFromAStreamAndReportsWhatItShows()
    {
        using var image = new XPlatImage();
        int failures = 0;
        image.AddLoadFailedHandler((_, _) => failures++);

        Assert.IsTrue(image.Load(PngBytes(4, 2)), "a PNG stream should load");
        Assert.IsNotNull(image.Image);
        Assert.AreEqual(4, image.Image!.Width);
        Assert.AreEqual(2, image.Image.Height);
        Assert.AreEqual(0, failures, "a successful load reports nothing");

        Assert.IsFalse(image.Load(new MemoryStream([1, 2, 3, 4])), "a stream that is not an image fails");

        string missing = Path.Combine(Path.GetTempPath(), $"xplatui-missing-{Guid.NewGuid():N}.png");
        Assert.IsFalse(image.Load(missing), "a missing file fails too");
        Assert.AreEqual(1, failures, "and the file failure is reported to the handler");
    }

    [TestMethod]
    public void AnImageSizeModeIsAssignable()
    {
        using var image = new XPlatImage();

        Assert.AreEqual(XPlatSizeMode.Zoom, image.SizeMode, "an image fills its box without distorting it");

        image.SizeMode = XPlatSizeMode.Zoom;
        Assert.AreEqual(XPlatSizeMode.Zoom, image.SizeMode);
    }

    [TestMethod]
    public void AToolStripHoldsItsItemsAndIsOneTabStop()
    {
        var strip = new XPlatStatusStrip { Dock = DockStyle.Bottom };
        var label = new XPlatToolStripLabel("Ready");
        var progress = new XPlatToolStripProgressBar { Value = 25f, Maximum = 100f };
        strip.Items.Add(label);
        strip.Items.Add(new XPlatToolStripSeparator());
        strip.Items.Add(new XPlatToolStripSpring());
        strip.Items.Add(progress);

        using var scene = HeadlessScene.Create(strip, 400f, 40f);

        Assert.AreEqual(4, strip.Items.Count);
        Assert.IsFalse(strip.TabStop, "a strip of labels and progress is nothing to tab to");
        Assert.AreEqual(25f, progress.Value);
        Assert.AreEqual(0.25f, progress.Percent);

        var button = new XPlatToolStripButton("Go");
        Assert.IsTrue(button.IsInteractive, "a button item is interactive");
        Assert.IsFalse(label.IsInteractive, "a label item is not");

        strip.Items.Add(button);
        Assert.IsTrue(strip.TabStop, "adding an interactive item makes the strip one tab stop");
    }

    /// <summary>
    /// Shows the scene's window and runs <paramref name="body"/> once it is on screen, which is the
    /// only moment a window is really visible: <see cref="XPlatWindow.Show"/> outside
    /// <see cref="XPlatApplication.Run"/> only registers it.
    /// </summary>
    private static void WhenShown(HeadlessScene scene, Action body)
    {
        scene.Window.AddShownHandler(_ => body());
        scene.Window.Show();
        scene.Application.Run();
    }

    /// <summary>Encodes a small solid PNG so the image control has real bytes to decode.</summary>
    private static MemoryStream PngBytes(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Red);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;
        return stream;
    }
}

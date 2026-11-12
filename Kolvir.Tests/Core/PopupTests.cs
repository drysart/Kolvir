using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The controls that show something in a popup - the combo box's list, the date picker's calendar
/// and the tool tip - and the popup's own open/close state.
/// </summary>
/// <remarks>
/// <para>
/// No platform popup service is registered on a headless machine, so these run the library's
/// fallback: the popup is painted inside the owning window's overlay layer. That is the path that
/// has to work everywhere, and it is the one an application gets before a backend registers itself.
/// </para>
/// <para>
/// The tool tip is the only time-based item here. It does not read
/// <see cref="UndoClock"/> - a timer asks the application backend for its wait - so the test
/// supplies a backend whose timer it fires by hand (<see cref="TestApplicationBackend"/>). Nothing
/// sleeps, and the delay asserted is the interval the control asked for.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class PopupTests : HeadlessTestBase
{
    [TestMethod]
    public void AComboBoxDropDownOpensAndCloses()
    {
        var combo = new ComboBox { Dock = DockStyle.Top };
        combo.Items.AddRange(new object?[] { "Small", "Medium", "Large" });
        combo.SelectedIndex = 1;

        using var scene = HeadlessScene.Create(combo, 240f, 200f);

        Assert.IsFalse(combo.IsDropDownOpen);
        Assert.IsNull(combo.DropDownList.Host, "the list is not hosted until the drop-down opens");

        combo.OpenDropDown();

        Assert.IsTrue(combo.IsDropDownOpen);
        Assert.IsNotNull(combo.DropDownList.Host, "the open list is hosted by the popup");
        Assert.AreSame(combo.DropDownList.Host, combo.DropDownList.Host);

        combo.CloseDropDown();

        Assert.IsFalse(combo.IsDropDownOpen, "CloseDropDown should close it");
    }

    [TestMethod]
    public void ADisabledComboBoxDoesNotOpenItsDropDown()
    {
        var combo = new ComboBox { Dock = DockStyle.Top };
        combo.Items.Add("Only");

        using var scene = HeadlessScene.Create(combo, 240f, 200f);

        combo.Enabled = false;
        combo.OpenDropDown();

        Assert.IsFalse(combo.IsDropDownOpen, "a disabled combo box takes no input");
        Assert.IsNull(combo.DropDownList.Host);
    }

    [TestMethod]
    public void PickingInTheOpenListUpdatesTheField()
    {
        var combo = new ComboBox { Dock = DockStyle.Top };
        combo.Items.AddRange(new object?[] { "Red", "Green", "Blue" });

        using var scene = HeadlessScene.Create(combo, 240f, 200f);
        combo.OpenDropDown();

        combo.DropDownList.SelectedIndex = 2;

        Assert.AreEqual("Blue", combo.SelectedText, "the field follows the list while it is open");
        Assert.AreEqual("Blue", combo.Text);
        Assert.AreEqual(2, combo.SelectedIndex);
    }

    [TestMethod]
    public void ADatePickerCalendarOpensAndCloses()
    {
        var picker = new DateTimePicker { Value = new DateTime(2025, 3, 4) };

        using var scene = HeadlessScene.Create(picker, 240f, 220f);

        Assert.IsFalse(picker.IsDropDownOpen);

        picker.OpenDropDown();

        Assert.IsTrue(picker.IsDropDownOpen);
        Assert.AreEqual(new DateTime(2025, 3, 4), picker.Value, "opening the calendar does not change the value");

        picker.CloseDropDown();

        Assert.IsFalse(picker.IsDropDownOpen);
    }

    [TestMethod]
    public void ADisabledDatePickerDoesNotOpenItsCalendar()
    {
        var picker = new DateTimePicker { Enabled = false, Value = new DateTime(2025, 3, 4) };

        using var scene = HeadlessScene.Create(picker, 240f, 220f);

        picker.OpenDropDown();

        Assert.IsFalse(picker.IsDropDownOpen);
    }

    [TestMethod]
    public void OpeningTheCalendarCommitsAHalfTypedDateFirst()
    {
        using var culture = CultureScope.English();

        var picker = new DateTimePicker();
        using var scene = HeadlessScene.Create(picker, 240f, 220f);
        picker.Focus();

        picker.SelectAll();
        scene.Type("6/7/2025");
        Assert.IsNull(picker.Value, "a typed date is not committed on every keystroke");

        picker.OpenDropDown();

        Assert.AreEqual(new DateTime(2025, 6, 7), picker.Value, "opening the calendar commits what the field shows");
    }

    [TestMethod]
    public void AToolTipWaitsForItsDelayBeforeShowing()
    {
        var backend = new TestApplicationBackend();
        var button = new Button("Go") { Dock = DockStyle.Fill };

        using var scene = HeadlessScene.Create(backend, button, 300f, 100f);
        using var toolTip = new ToolTip { InitialDelay = 600, AutoPopDelay = 5_000 };
        toolTip.SetToolTip(button, "A tip");

        scene.MoveTo(button);

        Assert.IsFalse(toolTip.IsVisible, "nothing is shown until the delay has passed");
        Assert.IsTrue(backend.Timer.IsRunning, "the hover should have started the delay");
        Assert.AreEqual(TimeSpan.FromMilliseconds(600), backend.Timer.Interval, "the delay is InitialDelay");

        backend.Timer.Fire();
        backend.Drain();

        Assert.IsTrue(toolTip.IsVisible, "the tick should show the tip");
        Assert.AreEqual(TimeSpan.FromMilliseconds(5_000), backend.Timer.Interval, "and restart the timer for AutoPopDelay");
    }

    [TestMethod]
    public void AToolTipHidesItselfWhenItsTimeIsUp()
    {
        var backend = new TestApplicationBackend();
        var button = new Button("Go") { Dock = DockStyle.Fill };

        using var scene = HeadlessScene.Create(backend, button, 300f, 100f);
        using var toolTip = new ToolTip { InitialDelay = 100, AutoPopDelay = 2_000 };
        toolTip.SetToolTip(button, "A tip");

        scene.MoveTo(button);
        backend.Timer.Fire();
        backend.Drain();
        Assert.IsTrue(toolTip.IsVisible);

        backend.Timer.Fire();
        backend.Drain();

        Assert.IsFalse(toolTip.IsVisible, "the second interval hides the tip again");
    }

    [TestMethod]
    public void LeavingBeforeTheDelayCancelsTheTip()
    {
        var backend = new TestApplicationBackend();
        var button = new Button("Go") { Dock = DockStyle.Top, Height = 40f };
        var panel = new Panel { Dock = DockStyle.Fill, Padding = Thickness.Empty, BorderWidth = 0f };
        panel.Controls.Add(button);

        using var scene = HeadlessScene.Create(backend, panel, 300f, 200f);
        using var toolTip = new ToolTip { InitialDelay = 600 };
        toolTip.SetToolTip(button, "A tip");

        scene.MoveTo(button);
        Assert.IsTrue(backend.Timer.IsRunning);

        scene.MoveTo(new SKPoint(10f, 180f));

        backend.Timer.Fire();
        backend.Drain();

        Assert.IsFalse(toolTip.IsVisible, "leaving before the delay should mean no tip at all");
    }

    [TestMethod]
    public void AnInactiveToolTipNeitherShowsNorWastesATimer()
    {
        var backend = new TestApplicationBackend();
        var button = new Button("Go") { Dock = DockStyle.Fill };

        using var scene = HeadlessScene.Create(backend, button, 300f, 100f);
        using var toolTip = new ToolTip { Active = false };
        toolTip.SetToolTip(button, "A tip");

        scene.MoveTo(button);

        Assert.IsFalse(backend.Timer.IsRunning, "an inactive tool tip should not start a delay");
        Assert.IsFalse(toolTip.IsVisible);
        Assert.AreEqual("A tip", toolTip.GetToolTip(button), "the text is still remembered");
    }

    [TestMethod]
    public void HidingTheControlsContainerCancelsThePendingTipAndItsDelay()
    {
        var backend = new TestApplicationBackend();
        var button = new Button("Go") { Dock = DockStyle.Fill };
        var panel = new Panel { Dock = DockStyle.Fill, Padding = Thickness.Empty, BorderWidth = 0f };
        panel.Controls.Add(button);

        using var scene = HeadlessScene.Create(backend, panel, 300f, 120f);
        using var toolTip = new ToolTip { InitialDelay = 600 };
        toolTip.SetToolTip(button, "A tip");

        scene.MoveTo(button);
        Assert.IsTrue(backend.Timer.IsRunning, "the hover starts the delay");

        panel.Visible = false;

        Assert.IsFalse(backend.Timer.IsRunning, "a control nobody can see has no tip to wait for");
        Assert.IsFalse(toolTip.IsVisible);

        backend.Timer.Fire();
        backend.Drain();

        Assert.IsFalse(toolTip.IsVisible, "and the delay that was already running cannot bring one back");
    }

    [TestMethod]
    public void HidingTheControlsContainerTakesADrawnTipAndItsDelayDown()
    {
        var backend = new TestApplicationBackend();
        var button = new Button("Go") { Dock = DockStyle.Fill };
        var panel = new Panel { Dock = DockStyle.Fill, Padding = Thickness.Empty, BorderWidth = 0f };
        panel.Controls.Add(button);

        using var scene = HeadlessScene.Create(backend, panel, 300f, 120f);
        using var toolTip = new ToolTip { InitialDelay = 100, AutoPopDelay = 5_000 };
        toolTip.SetToolTip(button, "A tip");

        scene.MoveTo(button);
        backend.Timer.Fire();
        backend.Drain();

        Assert.IsTrue(toolTip.IsVisible);
        Assert.IsTrue(backend.Timer.IsRunning, "the auto-pop delay is counting down");

        panel.Visible = false;

        Assert.IsFalse(toolTip.IsVisible, "the bubble belongs to a control that is gone from the screen");
        Assert.IsFalse(backend.Timer.IsRunning, "and nothing is left counting down for it");
    }

    [TestMethod]
    public void ClosingTheWindowTakesTheTipAndItsDelayDown()
    {
        var backend = new TestApplicationBackend();
        var button = new Button("Go") { Dock = DockStyle.Fill };

        using var scene = HeadlessScene.Create(backend, button, 300f, 100f);
        using var toolTip = new ToolTip { InitialDelay = 100, AutoPopDelay = 5_000 };
        toolTip.SetToolTip(button, "A tip");

        scene.RunWhileShown(() =>
        {
            scene.MoveTo(button);
            backend.Timer.Fire();
            backend.Drain();
            Assert.IsTrue(toolTip.IsVisible);

            scene.Window.Close();

            Assert.IsFalse(toolTip.IsVisible, "the bubble goes with the window");
            Assert.IsFalse(backend.Timer.IsRunning, "and so does the delay that would have hidden it");
        });
    }

    [TestMethod]
    public void AZeroDelayShowsTheTipAtOnce()
    {
        var backend = new TestApplicationBackend();
        var button = new Button("Go") { Dock = DockStyle.Fill };

        using var scene = HeadlessScene.Create(backend, button, 300f, 100f);
        using var toolTip = new ToolTip { InitialDelay = 0, AutoPopDelay = 5_000 };
        toolTip.SetToolTip(button, "A tip");

        scene.MoveTo(button);

        Assert.IsTrue(toolTip.IsVisible, "InitialDelay of zero means as soon as the pointer arrives");
        Assert.AreEqual(TimeSpan.FromMilliseconds(5_000), backend.Timer.Interval, "and only the hide delay is timed");
    }
}

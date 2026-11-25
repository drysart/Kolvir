using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The controls that show something in a popup - the combo box's list, the date picker's calendar
/// and the tool tip - the popup API that shows an arbitrary control and reports how it ended, and
/// the popup's own open/close state.
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
        var panel = new Panel { Dock = DockStyle.Fill, Padding = Thickness.Empty };
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
        var panel = new Panel { Dock = DockStyle.Fill, Padding = Thickness.Empty };
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
        var panel = new Panel { Dock = DockStyle.Fill, Padding = Thickness.Empty };
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

    // The popup API: ShowPopupAsync, ClosePopup and the closed handlers. Nothing here awaits,
    // because a headless run drains its queue once rather than looping; a task completes
    // synchronously and its continuation is what is queued.

    [TestMethod]
    public void ClosingAPopupFromApplicationCodeCompletesWithClosed()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var content = new Panel();
        var task = anchor.ShowPopupAsync(content, SizedPopup());

        Assert.IsNotNull(content.Host, "the popup hosts the content");
        Assert.IsFalse(task.IsCompleted, "nothing has dismissed it yet");

        content.ClosePopup();

        Assert.IsTrue(task.IsCompletedSuccessfully, "closing it completes the task");
        Assert.AreEqual(PopupCloseReason.Closed, task.Result.Reason);
        Assert.IsNull(content.Host, "the content leaves the popup when it closes");
    }

    [TestMethod]
    public void TheAwaitingCallerResumesOnTheUiThreadAfterTheClose()
    {
        var backend = new TestApplicationBackend();
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(backend, root, 480f, 320f);

        var content = new Panel();
        PopupResult? result = null;
        var continued = false;
        int uiThreadId = 0;
        var continuationThreadId = 0;

        scene.RunWhileShown(() =>
        {
            uiThreadId = Environment.CurrentManagedThreadId;

            var task = anchor.ShowPopupAsync(content, SizedPopup());
            var body = AwaitPopupAsync(task, closed =>
            {
                continued = true;
                continuationThreadId = Environment.CurrentManagedThreadId;
                result = closed;
            });

            content.ClosePopup();

            Assert.IsFalse(continued, "the completion is queued, so application code never runs inside the close path");

            backend.Drain();

            Assert.IsTrue(continued, "the await resumes when the queue is drained");
            Assert.AreEqual(uiThreadId, continuationThreadId, "and it resumes on the UI thread");
            Assert.AreEqual(PopupCloseReason.Closed, result?.Reason);

            GC.KeepAlive(body);
        });
    }

    [TestMethod]
    public void ClosingAPopupWithAValueHandsItToTheCaller()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var content = new Panel();
        var task = anchor.ShowPopupAsync(content, SizedPopup());

        content.ClosePopup(PopupResult.Closed("Medium"));

        Assert.AreEqual(PopupCloseReason.Closed, task.Result.Reason);
        Assert.AreEqual("Medium", task.Result.Value);
        Assert.IsTrue(task.Result.TryGetValue<string>(out var value), "the value comes back as the type it went in as");
        Assert.AreEqual("Medium", value);
        Assert.IsFalse(task.Result.TryGetValue<int>(out _), "and as no other type");
    }

    [TestMethod]
    public void ClosingAPopupWithoutAValueCarriesNothing()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var content = new Panel();
        var task = anchor.ShowPopupAsync(content, SizedPopup());

        content.ClosePopup();

        Assert.AreEqual(PopupCloseReason.Closed, task.Result.Reason);
        Assert.IsNull(task.Result.Value);
        Assert.IsFalse(task.Result.TryGetValue<string>(out _), "there is nothing to read");
    }

    [TestMethod]
    public void ClosingAControlThatIsNotInAPopupDoesNothing()
    {
        var content = new Panel();

        content.ClosePopup();
        content.ClosePopup(PopupResult.Closed("ignored"));

        Assert.IsNull(content.Host);
    }

    [TestMethod]
    public void APressOutsideThePopupClosesItAndIsNotDelivered()
    {
        var (root, anchor, other) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var clicks = 0;
        other.AddClickHandler(_ => clicks++);

        var content = new Panel();
        var task = anchor.ShowPopupAsync(content, SizedPopup());

        scene.ClickAt(scene.CenterOf(other));

        Assert.IsTrue(task.IsCompletedSuccessfully, "a press outside the popup dismisses it");
        Assert.AreEqual(PopupCloseReason.OutsideClick, task.Result.Reason);
        Assert.AreEqual(0, clicks, "the press that dismisses a popup belongs to the popup");
    }

    [TestMethod]
    public void ClosingTheOwnerWindowCompletesWithOwnerClosed()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var content = new Panel();
        var task = anchor.ShowPopupAsync(content, SizedPopup());

        scene.Window.Close();

        Assert.IsTrue(task.IsCompletedSuccessfully, "the popup cannot outlive the window it belongs to");
        Assert.AreEqual(PopupCloseReason.OwnerClosed, task.Result.Reason);
    }

    [TestMethod]
    public void HidingTheAnchorCompletesWithOwnerClosed()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var content = new Panel();
        Task<PopupResult>? task = null;

        scene.RunWhileShown(() =>
        {
            task = anchor.ShowPopupAsync(content, SizedPopup());
            Assert.IsTrue(anchor.IsDisplayed, "the anchor is on screen before it goes away");
            Assert.IsFalse(task.IsCompleted);

            anchor.Visible = false;

            Assert.IsTrue(task.IsCompleted, "a control nobody can see has no popup");
            Assert.AreEqual(PopupCloseReason.OwnerClosed, task.Result.Reason);
        });

        Assert.IsNotNull(task);
    }

    [TestMethod]
    public void ASecondPopupOnTheSameSurfaceCancelsTheFirst()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var first = new Panel();
        var second = new Panel();

        var firstTask = anchor.ShowPopupAsync(first, SizedPopup());
        var secondTask = anchor.ShowPopupAsync(second, SizedPopup());

        Assert.IsTrue(firstTask.IsCompletedSuccessfully, "the first popup is taken down to make room");
        Assert.AreEqual(PopupCloseReason.Cancelled, firstTask.Result.Reason);
        Assert.IsNull(first.Host, "and its content is released");
        Assert.IsFalse(secondTask.IsCompleted, "the second popup is the one that stays up");
        Assert.IsNotNull(second.Host);
    }

    [TestMethod]
    public void APopupOpenedInsideAPopupCancelsOnlyItsOwnLevel()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 4f, 124f, 34f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(innerAnchor);

        var outerTask = anchor.ShowPopupAsync(outerContent, SizedPopup());

        var innerContent = new Panel();
        var innerTask = innerAnchor.ShowPopupAsync(innerContent, SizedPopup());

        Assert.IsFalse(outerTask.IsCompleted, "the outer popup is still open");
        Assert.IsFalse(innerTask.IsCompleted, "and so is the one opened inside it");

        var replacementContent = new Panel();
        var replacementTask = innerAnchor.ShowPopupAsync(replacementContent, SizedPopup());

        Assert.AreEqual(
            PopupCloseReason.Cancelled,
            innerTask.Result.Reason,
            "only the level the popup was opened from is replaced");
        Assert.IsFalse(outerTask.IsCompleted, "the level above it is untouched");
        Assert.IsFalse(replacementTask.IsCompleted);
    }

    [TestMethod]
    public void ClosingAPopupClosesThePopupOpenedInsideIt()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 4f, 124f, 34f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(innerAnchor);

        var outerTask = anchor.ShowPopupAsync(outerContent, SizedPopup());
        var innerTask = innerAnchor.ShowPopupAsync(new Panel(), SizedPopup());

        outerContent.ClosePopup();

        Assert.AreEqual(PopupCloseReason.Closed, outerTask.Result.Reason);
        Assert.IsTrue(innerTask.IsCompletedSuccessfully, "a popup opened inside it goes with it");
        Assert.AreEqual(PopupCloseReason.OwnerClosed, innerTask.Result.Reason);
    }

    [TestMethod]
    public void AClosedHandlerFiresForEveryPopupTheControlIsShownIn()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var content = new Panel();
        var senders = new List<object>();
        var reasons = new List<PopupCloseReason>();

        content.AddPopupClosedHandler((sender, result) =>
        {
            senders.Add(sender);
            reasons.Add(result.Reason);
        });

        anchor.ShowPopupAsync(content, SizedPopup());
        content.ClosePopup(PopupResult.Closed(1));

        anchor.ShowPopupAsync(content, SizedPopup());
        content.ClosePopup();

        Assert.AreEqual(2, reasons.Count, "the handler outlives the popup it was added for");
        Assert.AreEqual(PopupCloseReason.Closed, reasons[0]);
        Assert.AreEqual(PopupCloseReason.Closed, reasons[1]);
        Assert.AreSame(content, senders[0], "the sender is the popup's content control");
        Assert.AreSame(content, senders[1]);
    }

    [TestMethod]
    public void ARemovedClosedHandlerStopsFiring()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var content = new Panel();
        var calls = 0;
        Action<object, PopupResult> handler = (_, _) => calls++;

        content.AddPopupClosedHandler(handler);
        anchor.ShowPopupAsync(content, SizedPopup());
        content.ClosePopup();
        Assert.AreEqual(1, calls);

        content.RemovePopupClosedHandler(handler);
        anchor.ShowPopupAsync(content, SizedPopup());
        content.ClosePopup();
        Assert.AreEqual(1, calls, "a removed handler hears nothing");

        using (content.AddPopupClosedHandler(handler))
        {
            anchor.ShowPopupAsync(content, SizedPopup());
            content.ClosePopup();
            Assert.AreEqual(2, calls);
        }

        anchor.ShowPopupAsync(content, SizedPopup());
        content.ClosePopup();
        Assert.AreEqual(2, calls, "disposing the subscription removes it");
    }

    [TestMethod]
    public void AContentControlThatIsAlreadyInAControlTreeCannotBeShown()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var content = new Panel();
        root.Controls.Add(content);

        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => anchor.ShowPopupAsync(content, SizedPopup()));

        StringAssert.Contains(error.Message, "already in a control tree");
    }

    [TestMethod]
    public void AContentControlAlreadyShownInAPopupCannotBeShownAgain()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var content = new Panel();
        anchor.ShowPopupAsync(content, SizedPopup());

        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => anchor.ShowPopupAsync(content, SizedPopup()));

        StringAssert.Contains(error.Message, "already shown in a popup");
    }

    [TestMethod]
    public void AnchorPlacementWithoutAnAnchorIsRejected()
    {
        var (root, _, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var error = Assert.ThrowsExactly<ArgumentException>(
            () => scene.Window.ShowPopupAsync(new Panel()));

        Assert.AreEqual("options", error.ParamName);
        StringAssert.Contains(error.Message, nameof(PopupPlacement.Anchor));
    }

    [TestMethod]
    public void AnAnchorThatIsNotInAWindowIsRejected()
    {
        var anchor = new Button("Nowhere");

        var error = Assert.ThrowsExactly<ArgumentException>(
            () => anchor.ShowPopupAsync(new Panel()));

        Assert.AreEqual("anchor", error.ParamName, "the anchor is the argument at fault");
    }

    [TestMethod]
    public void AnAnchorPopupOpensUnderTheAnchorPlusTheOffset()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var content = new Panel();
        anchor.ShowPopupAsync(content, new PopupOptions
        {
            Size = new SKSize(140f, 90f),
            Offset = new SKPoint(4f, 6f),
        });

        var origin = anchor.GetPositionInHost();
        Assert.AreEqual(
            new SKPoint(origin.X + 4f, origin.Y + anchor.Height + 6f),
            content.GetPositionInHost(),
            "the popup hangs from the anchor's bottom-left corner");
    }

    [TestMethod]
    public void AClientPointPopupOpensAtThePoint()
    {
        var (root, _, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var content = new Panel();
        var task = scene.Window.ShowPopupAsync(content, new PopupOptions
        {
            Placement = PopupPlacement.ClientPoint,
            Offset = new SKPoint(64f, 48f),
            Size = new SKSize(100f, 60f),
        });

        Assert.AreEqual(new SKPoint(64f, 48f), content.GetPositionInHost());

        content.ClosePopup();
        Assert.AreEqual(PopupCloseReason.Closed, task.Result.Reason, "a window's popup closes like any other");
    }

    [TestMethod]
    public void APointerPopupOpensAtThePointer()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        scene.MoveTo(new SKPoint(200f, 150f));

        var content = new Panel();
        anchor.ShowPopupAsync(content, new PopupOptions
        {
            Placement = PopupPlacement.Pointer,
            Size = new SKSize(100f, 60f),
        });

        Assert.AreEqual(new SKPoint(200f, 150f), content.GetPositionInHost());
    }

    /// <summary>A window with an anchor near the top left and another button well away from it.</summary>
    private static (Panel Root, Button Anchor, Button Other) CreatePopupTree()
    {
        var anchor = new Button("Anchor") { Bounds = new SKRect(20f, 20f, 120f, 50f) };
        var other = new Button("Other") { Bounds = new SKRect(240f, 240f, 340f, 270f) };
        var root = new Panel { Dock = DockStyle.Fill, Padding = Thickness.Empty };

        root.Controls.Add(anchor);
        root.Controls.Add(other);

        return (root, anchor, other);
    }

    /// <summary>Options with a fixed size, so placement is what a test measures rather than text.</summary>
    private static PopupOptions SizedPopup() => new() { Size = new SKSize(140f, 90f) };

    /// <summary>
    /// What an application's <c>await ShowPopupAsync(...)</c> becomes: it resumes under whatever
    /// context was current when it suspended, and reports the thread it resumed on.
    /// </summary>
    private static async Task AwaitPopupAsync(Task<PopupResult> popup, Action<PopupResult> onClosed) =>
        onClosed(await popup);
}

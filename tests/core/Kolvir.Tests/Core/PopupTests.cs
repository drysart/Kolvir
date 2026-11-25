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
            () => scene.Window.ShowPopupAsync(new Panel(), new PopupOptions { Placement = PopupPlacement.Anchor }));

        Assert.AreEqual("options", error.ParamName);
        StringAssert.Contains(error.Message, nameof(PopupPlacement.Anchor));
    }

    [TestMethod]
    public void AutoResolvesToThePointerWhenTheCallSuppliedNoAnchor()
    {
        var (root, _, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        scene.MoveTo(new SKPoint(200f, 150f));

        var content = new Panel();
        scene.Window.ShowPopupAsync(content, SizedPopup());

        Assert.AreEqual(
            new SKPoint(200f, 150f),
            content.GetPositionInHost(),
            "with no anchor, Auto is the pointer the user is looking at");
    }

    [TestMethod]
    public void AutoResolvesToTheAnchorWhenTheCallSuppliedOne()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        scene.MoveTo(new SKPoint(300f, 200f));

        var content = new Panel();
        anchor.ShowPopupAsync(content, SizedPopup());

        Assert.AreEqual(
            new SKPoint(20f, 50f),
            content.GetPositionInHost(),
            "with an anchor, Auto hangs the popup from it and ignores the pointer");
    }

    [TestMethod]
    public void EveryAlignPlacesThePopupAgainstTheAnchor()
    {
        var (root, anchor) = CreateAnchorTree(new SKRect(200f, 150f, 300f, 180f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        scene.MoveTo(new SKPoint(100f, 120f));

        var cases = new (PopupAlign Align, SKPoint Location)[]
        {
            (PopupAlign.BelowStart, new SKPoint(200f, 180f)),
            (PopupAlign.BelowCenter, new SKPoint(230f, 180f)),
            (PopupAlign.BelowEnd, new SKPoint(260f, 180f)),
            (PopupAlign.AboveStart, new SKPoint(200f, 120f)),
            (PopupAlign.AboveCenter, new SKPoint(230f, 120f)),
            (PopupAlign.AboveEnd, new SKPoint(260f, 120f)),
            (PopupAlign.RightOf, new SKPoint(300f, 150f)),
            (PopupAlign.LeftOf, new SKPoint(160f, 150f)),
            (PopupAlign.AtPointer, new SKPoint(100f, 120f)),
        };

        foreach (var (align, location) in cases)
        {
            var content = new Panel();
            anchor.ShowPopupAsync(content, new PopupOptions { Align = align, Size = new SKSize(40f, 30f) });

            Assert.AreEqual(location, content.GetPositionInHost(), $"{align} should place the popup at {location}");

            content.ClosePopup();
        }
    }

    [TestMethod]
    public void AClientPointPopupUsesTheAlignAroundThePoint()
    {
        var (root, _, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var content = new Panel();
        scene.Window.ShowPopupAsync(content, new PopupOptions
        {
            Placement = PopupPlacement.ClientPoint,
            Align = PopupAlign.AboveCenter,
            Offset = new SKPoint(200f, 150f),
            Size = new SKSize(40f, 30f),
        });

        Assert.AreEqual(
            new SKPoint(180f, 120f),
            content.GetPositionInHost(),
            "the point acts as a zero-sized anchor, so AboveCenter sits above it and centred on it");
    }

    [TestMethod]
    public void ABelowPopupAtTheBottomOfTheAreaFlipsAbove()
    {
        var (root, anchor) = CreateAnchorTree(new SKRect(20f, 270f, 120f, 300f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var content = new Panel();
        anchor.ShowPopupAsync(content, new PopupOptions { Size = new SKSize(140f, 90f) });

        Assert.AreEqual(
            new SKPoint(20f, 180f),
            content.GetPositionInHost(),
            "there is no room below, so the popup opens above the anchor instead");
    }

    [TestMethod]
    public void APopupThatFitsNeitherWayIsClampedIntoTheArea()
    {
        var (root, anchor) = CreateAnchorTree(new SKRect(20f, 150f, 120f, 180f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var content = new Panel();
        anchor.ShowPopupAsync(content, new PopupOptions { Size = new SKSize(140f, 200f) });

        Assert.AreEqual(
            new SKPoint(20f, 0f),
            content.GetPositionInHost(),
            "it flips above and still overhangs, so the clamp pins it to the top of the area");
    }

    [TestMethod]
    public void WithFlippingOffThePopupSlidesIntoTheAreaInstead()
    {
        var (root, anchor) = CreateAnchorTree(new SKRect(20f, 270f, 120f, 300f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var content = new Panel();
        anchor.ShowPopupAsync(content, new PopupOptions
        {
            Size = new SKSize(140f, 90f),
            FlipIntoView = false,
        });

        Assert.AreEqual(
            new SKPoint(20f, 230f),
            content.GetPositionInHost(),
            "with no flip the popup stays below the anchor and the clamp is what keeps it in the window");
    }

    [TestMethod]
    public void APopupUsesThePlacementAreaItIsGiven()
    {
        var (root, anchor) = CreateAnchorTree(new SKRect(20f, 270f, 120f, 300f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var service = new RecordingPopupService(new SKRect(0f, 0f, 480f, 400f));
        OverridePopupService(scene, service);

        var content = new Panel();
        anchor.ShowPopupAsync(content, new PopupOptions { Size = new SKSize(140f, 90f) });

        Assert.AreEqual(
            new SKPoint(20f, 300f),
            content.GetPositionInHost(),
            "the work area has room below the window's bottom edge, so no flip is needed");
        Assert.AreEqual(1, service.AreaQueries, "the popup asked the service for the area once");
        Assert.AreSame(scene.Host, service.LastHost, "and asked about the host it belongs to");
    }

    [TestMethod]
    public void ANullPlacementAreaKeepsThePopupInsideTheWindow()
    {
        var (root, anchor) = CreateAnchorTree(new SKRect(400f, 270f, 470f, 300f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var service = new RecordingPopupService(area: null);
        OverridePopupService(scene, service);

        var content = new Panel();
        anchor.ShowPopupAsync(content, new PopupOptions { Size = new SKSize(140f, 90f) });

        Assert.AreEqual(
            new SKPoint(340f, 180f),
            content.GetPositionInHost(),
            "null means the popup cannot leave the window, so it flips and then clamps to the client area");
        Assert.AreEqual(1, service.AreaQueries);
    }

    [TestMethod]
    public void APopupInAZoomedSubtreeIsScaledForPlacementToo()
    {
        var zoom = new Panel { Dock = DockStyle.Fill, Padding = Thickness.Empty, ScaleFactor = 2f };
        var anchor = new Button("Zoomed") { Bounds = new SKRect(40f, 60f, 100f, 80f) };
        zoom.Controls.Add(anchor);

        using var scene = HeadlessScene.Create(zoom, 480f, 320f);

        var content = new Panel();
        anchor.ShowPopupAsync(content, new PopupOptions { Size = new SKSize(40f, 30f) });

        Assert.AreEqual(
            new SKPoint(80f, 160f),
            content.GetPositionInHost(),
            "the anchor's host position and its doubled height decide where the popup hangs");
    }

    [TestMethod]
    public void APopupOpenedInsideAPopupIsPlacedInTheOuterPopupsSpace()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 4f, 124f, 34f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(innerAnchor);

        anchor.ShowPopupAsync(outerContent, new PopupOptions { Size = new SKSize(200f, 150f) });
        var outerOrigin = outerContent.GetPositionInHost();

        var innerContent = new Panel();
        innerAnchor.ShowPopupAsync(innerContent, new PopupOptions { Size = new SKSize(120f, 60f) });

        Assert.AreEqual(
            new SKPoint(outerOrigin.X + 4f, outerOrigin.Y + 34f),
            innerContent.GetPositionInHost(),
            "the inner anchor's own bounds are relative to the popup it sits in, not the window");
    }

    [TestMethod]
    public void APopupOpenedInsideAPopupFlipsInTheOuterPopupsSpace()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 120f, 124f, 150f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(innerAnchor);

        anchor.ShowPopupAsync(outerContent, new PopupOptions { Size = new SKSize(200f, 150f) });
        Assert.AreEqual(
            new SKPoint(20f, 50f),
            outerContent.GetPositionInHost(),
            "the outer popup fits under the window anchor");

        var innerContent = new Panel();
        innerAnchor.ShowPopupAsync(innerContent, new PopupOptions { Size = new SKSize(120f, 160f) });

        Assert.AreEqual(
            new SKPoint(24f, 10f),
            innerContent.GetPositionInHost(),
            "below the inner anchor would end at host y 360, so it flips above the anchor at y 170");
    }

    // Where a popup landed. The options are the request; TryGetPopupBounds is the answer to what
    // the display did with it, and it is read in the owning window's client coordinates so a caller
    // never has to know which level of a popup chain the control it holds is at.

    [TestMethod]
    public void TheResolvedBoundsOfAShownPopupAreReadable()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var content = new Panel();
        anchor.ShowPopupAsync(content, new PopupOptions { Size = new SKSize(140f, 90f) });

        Assert.IsTrue(content.TryGetPopupBounds(out var bounds), "a shown popup has a rectangle");
        Assert.AreEqual(
            new SKRect(20f, 50f, 160f, 140f),
            bounds,
            "the anchor is at (20,20)-(120,50), so the default align hangs the popup under it");
    }

    [TestMethod]
    public void EveryAlignIsCarriedThroughTheShowToTheRectangleThePopupOccupies()
    {
        var anchor = new Button("Anchor") { Bounds = new SKRect(200f, 100f, 300f, 130f) };
        var root = new Panel { Dock = DockStyle.Fill, Padding = Thickness.Empty };
        root.Controls.Add(anchor);

        using var scene = HeadlessScene.Create(root, 480f, 320f);

        // The pointer sits well away from the anchor, so AtPointer has a place of its own to land on
        // and cannot be mistaken for one of the aligns measured against the anchor.
        scene.MoveTo(new SKPoint(400f, 220f));

        var expected = new Dictionary<PopupAlign, SKRect>
        {
            [PopupAlign.BelowStart] = new SKRect(200f, 130f, 260f, 190f),
            [PopupAlign.BelowCenter] = new SKRect(220f, 130f, 280f, 190f),
            [PopupAlign.BelowEnd] = new SKRect(240f, 130f, 300f, 190f),
            [PopupAlign.AboveStart] = new SKRect(200f, 40f, 260f, 100f),
            [PopupAlign.AboveCenter] = new SKRect(220f, 40f, 280f, 100f),
            [PopupAlign.AboveEnd] = new SKRect(240f, 40f, 300f, 100f),
            [PopupAlign.RightOf] = new SKRect(300f, 100f, 360f, 160f),
            [PopupAlign.LeftOf] = new SKRect(140f, 100f, 200f, 160f),
            [PopupAlign.AtPointer] = new SKRect(400f, 220f, 460f, 280f),
        };

        var seen = new List<SKRect>();

        foreach (var align in Enum.GetValues<PopupAlign>())
        {
            var content = new Panel();
            anchor.ShowPopupAsync(content, new PopupOptions { Align = align, Size = new SKSize(60f, 60f) });

            Assert.IsTrue(content.TryGetPopupBounds(out var bounds), $"{align}: a shown popup has a rectangle");
            Assert.AreEqual(expected[align], bounds, $"{align} is not where its own align asks for");

            seen.Add(bounds);
            content.ClosePopup();
        }

        Assert.AreEqual(9, seen.Distinct().Count(), "every align lands somewhere of its own");
    }

    [TestMethod]
    public void TheResolvedBoundsFollowTheFlip()
    {
        var (root, flippedAnchor) = CreateAnchorTree(new SKRect(20f, 270f, 120f, 300f));
        var fittingAnchor = new Button("Fits") { Bounds = new SKRect(200f, 40f, 300f, 70f) };
        root.Controls.Add(fittingAnchor);

        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var fitting = new Panel();
        fittingAnchor.ShowPopupAsync(fitting, new PopupOptions { Size = new SKSize(140f, 90f) });

        Assert.IsTrue(fitting.TryGetPopupBounds(out var unchanged));
        Assert.AreEqual(
            new SKRect(200f, 70f, 340f, 160f),
            unchanged,
            "with room below, the rectangle is where the align asked for");

        var flipped = new Panel();
        flippedAnchor.ShowPopupAsync(flipped, new PopupOptions { Size = new SKSize(140f, 90f) });

        Assert.IsTrue(flipped.TryGetPopupBounds(out var afterFlip));
        Assert.AreEqual(
            new SKRect(20f, 180f, 160f, 270f),
            afterFlip,
            "the anchor is 20 pixels from the bottom of a 320-tall window, so the popup opens above it");
        Assert.AreNotEqual(
            new SKRect(20f, 300f, 160f, 390f),
            afterFlip,
            "and not the rectangle the BelowStart align asked for");
    }

    [TestMethod]
    public void ABoundsReadForAPopupInsideAPopupIsInTheWindowsSpace()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var service = new RecordingPopupService(new SKRect(0f, 0f, 480f, 320f), serveWindows: true);
        OverridePopupService(scene, service);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 30f, 124f, 60f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(innerAnchor);

        anchor.ShowPopupAsync(outerContent, new PopupOptions { Size = new SKSize(200f, 150f) });

        var innerContent = new Panel();
        innerAnchor.ShowPopupAsync(innerContent, new PopupOptions { Size = new SKSize(120f, 60f) });

        Assert.IsTrue(innerContent.TryGetPopupBounds(out var bounds));
        Assert.AreEqual(
            new SKRect(service.Windows[1].Location.X, service.Windows[1].Location.Y,
                service.Windows[1].Location.X + 120f, service.Windows[1].Location.Y + 60f),
            bounds,
            "the read is the rectangle the platform was given, so a popup placed in its parent's "
            + "coordinates is reported in the window's without the caller adding the parent's origin");
        Assert.AreEqual(
            new SKRect(24f, 110f, 144f, 170f),
            bounds,
            "24,110 is where the inner popup sits in the window; 4,30 would be its place inside the outer one");
    }

    [TestMethod]
    public void TheResolvedBoundsIncludeTheScaleOfTheSubtree()
    {
        var zoom = new Panel { Dock = DockStyle.Fill, Padding = Thickness.Empty, ScaleFactor = 2f };
        var anchor = new Button("Zoomed") { Bounds = new SKRect(40f, 60f, 100f, 80f) };
        zoom.Controls.Add(anchor);

        using var scene = HeadlessScene.Create(zoom, 480f, 320f);

        var content = new Panel();
        anchor.ShowPopupAsync(content, new PopupOptions { Size = new SKSize(40f, 30f) });

        Assert.IsTrue(content.TryGetPopupBounds(out var bounds));
        Assert.AreEqual(
            new SKRect(80f, 160f, 160f, 220f),
            bounds,
            "the rectangle is the room the popup takes, so a 40x30 popup at 2.0 is 80x60");
    }

    [TestMethod]
    public void AControlThatIsNotInAPopupHasNoBoundsToReport()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var loose = new Panel { Bounds = new SKRect(10f, 10f, 60f, 40f) };
        root.Controls.Add(loose);

        Assert.IsFalse(loose.TryGetPopupBounds(out var none), "nothing is showing it in a popup");
        Assert.AreEqual(default(SKRect), none, "and the out parameter is left empty rather than stale");

        var content = new Panel();
        anchor.ShowPopupAsync(content, SizedPopup());
        Assert.IsTrue(content.TryGetPopupBounds(out _));

        content.ClosePopup();

        Assert.IsFalse(content.TryGetPopupBounds(out var closed), "a closed popup has no rectangle to report");
        Assert.AreEqual(default(SKRect), closed);
    }

    [TestMethod]
    public void APopupInsideAPopupIsHandedToThePlatformInTheWindowsCoordinates()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var service = new RecordingPopupService(new SKRect(0f, 0f, 480f, 320f), serveWindows: true);
        OverridePopupService(scene, service);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 30f, 124f, 60f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(innerAnchor);

        anchor.ShowPopupAsync(outerContent, new PopupOptions { Size = new SKSize(200f, 150f) });
        innerAnchor.ShowPopupAsync(new Panel(), new PopupOptions { Size = new SKSize(120f, 60f) });

        Assert.AreEqual(2, service.Windows.Count, "each level asked the platform for a surface");

        var outer = service.Windows[0];
        var inner = service.Windows[1];

        Assert.AreSame(scene.Host, service.LastCreatedFor, "the surface is asked of the window's host");
        Assert.AreSame(scene.Host, outer.ShownFor, "a popup opened from the window is shown against the window");
        Assert.AreSame(
            scene.Host,
            inner.ShownFor,
            "and one opened from inside a popup is shown against the same window, not against that popup");
        Assert.AreEqual(
            new SKPoint(20f, 50f),
            outer.Location,
            "the outer popup opens under the anchor in the window's own coordinates");
        Assert.AreEqual(
            new SKPoint(24f, 110f),
            inner.Location,
            "the inner popup's place inside the outer popup, moved by where the outer popup sits");
        Assert.AreEqual(new SKSize(120f, 60f), inner.Size, "the size the popup asked for needs no conversion");
    }

    [TestMethod]
    public void APopupInsideAPopupFitsAgainstTheDisplayNotThePopupItSitsIn()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        // The display's work area is the whole window here, so the only limit that can flip the
        // inner popup is the bottom of the display - the parent popup ends well above it.
        var service = new RecordingPopupService(new SKRect(0f, 0f, 480f, 320f), serveWindows: true);
        OverridePopupService(scene, service);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 200f, 124f, 230f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(innerAnchor);

        anchor.ShowPopupAsync(outerContent, new PopupOptions { Size = new SKSize(200f, 250f) });
        innerAnchor.ShowPopupAsync(new Panel(), new PopupOptions { Size = new SKSize(120f, 60f) });

        Assert.AreEqual(new SKPoint(20f, 50f), service.Windows[0].Location, "the outer popup sits under its anchor");
        Assert.AreEqual(
            new SKPoint(24f, 190f),
            service.Windows[1].Location,
            "below the inner anchor would end at window y 340, past the bottom of the display at y 320, so "
            + "the inner popup flips above it - a clamp to the parent popup, which ends at window y 300, "
            + "would have slid it up to y 240 instead");
        Assert.AreEqual(2, service.AreaQueries, "each level asked about the display once");
        Assert.AreSame(scene.Host, service.LastHost, "and asked about the window, whatever popup it was opened in");
    }

    // A press outside a chain of popups closes every level the press is outside of and stops at the
    // first level it is inside of - or at the first level that opted out of closing on an outside
    // press. Both presentations go through the same walk: the overlay catcher reports the press it
    // swallowed, and a platform window reports the press on the owning window, each with its point in
    // the window's client coordinates.

    [TestMethod]
    public void APressOutsideAChainOfOverlaidPopupsClosesEveryLevel()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 30f, 124f, 60f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(innerAnchor);

        var outerTask = anchor.ShowPopupAsync(outerContent, new PopupOptions { Size = new SKSize(200f, 150f) });
        var innerTask = innerAnchor.ShowPopupAsync(new Panel(), new PopupOptions { Size = new SKSize(120f, 60f) });

        scene.ClickAt(new SKPoint(400f, 300f));

        Assert.IsTrue(innerTask.IsCompletedSuccessfully, "the press is outside the innermost popup");
        Assert.AreEqual(PopupCloseReason.OutsideClick, innerTask.Result.Reason);
        Assert.IsTrue(outerTask.IsCompletedSuccessfully, "and it is outside the popup that one was opened from too");
        Assert.AreEqual(PopupCloseReason.OutsideClick, outerTask.Result.Reason);
    }

    [TestMethod]
    public void APressInsideTheParentPopupClosesOnlyTheChildOpenedFromIt()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 30f, 124f, 60f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(innerAnchor);

        var outerTask = anchor.ShowPopupAsync(outerContent, new PopupOptions { Size = new SKSize(200f, 150f) });
        var innerTask = innerAnchor.ShowPopupAsync(new Panel(), new PopupOptions { Size = new SKSize(120f, 60f) });

        // The outer popup ends at (220, 200) and the inner one at (144, 170), so this point is inside
        // the parent and outside the child.
        scene.ClickAt(new SKPoint(200f, 190f));

        Assert.IsTrue(innerTask.IsCompletedSuccessfully, "the press is outside the child");
        Assert.AreEqual(PopupCloseReason.OutsideClick, innerTask.Result.Reason);
        Assert.IsFalse(outerTask.IsCompleted, "and inside the parent, which is the level the user was working in");
    }

    [TestMethod]
    public void AWindowPopupReportsAnOutsidePressWithItsPointAndTheChainClosesEveryLevel()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var service = new RecordingPopupService(new SKRect(0f, 0f, 480f, 320f), serveWindows: true);
        OverridePopupService(scene, service);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 30f, 124f, 60f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(innerAnchor);

        var outerTask = anchor.ShowPopupAsync(outerContent, new PopupOptions { Size = new SKSize(200f, 150f) });
        var innerTask = innerAnchor.ShowPopupAsync(new Panel(), new PopupOptions { Size = new SKSize(120f, 60f) });

        // What the owning window's message procedure does with a press it receives.
        Assert.IsTrue(
            service.Windows[1].RaiseOutsidePress(new SKPoint(400f, 300f)),
            "the press was taken, so the window must not also deliver it");

        Assert.IsTrue(innerTask.IsCompletedSuccessfully, "the innermost level heard the press");
        Assert.IsTrue(outerTask.IsCompletedSuccessfully, "and so did the level it was opened from");
        Assert.AreEqual(PopupCloseReason.OutsideClick, innerTask.Result.Reason, "the innermost level reports the press");
        Assert.AreEqual(PopupCloseReason.OutsideClick, outerTask.Result.Reason, "and so does the level it was opened from");
        Assert.IsTrue(service.Windows[0].IsDisposed, "both surfaces are gone");
        Assert.IsTrue(service.Windows[1].IsDisposed);
    }

    [TestMethod]
    public void AWindowPopupPressInsideTheParentClosesOnlyTheChild()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var service = new RecordingPopupService(new SKRect(0f, 0f, 480f, 320f), serveWindows: true);
        OverridePopupService(scene, service);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 30f, 124f, 60f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(innerAnchor);

        var outerTask = anchor.ShowPopupAsync(outerContent, new PopupOptions { Size = new SKSize(200f, 150f) });
        var innerTask = innerAnchor.ShowPopupAsync(new Panel(), new PopupOptions { Size = new SKSize(120f, 60f) });

        Assert.IsTrue(service.Windows[1].RaiseOutsidePress(new SKPoint(200f, 190f)), "the press was taken by the child");

        Assert.IsTrue(innerTask.IsCompletedSuccessfully, "the point is outside the child");
        Assert.AreEqual(PopupCloseReason.OutsideClick, innerTask.Result.Reason);
        Assert.IsFalse(outerTask.IsCompleted, "the point is inside the parent, so the walk stops there");
        Assert.IsFalse(service.Windows[0].IsDisposed);
        Assert.IsTrue(service.Windows[1].IsDisposed);
    }

    [TestMethod]
    public void APressInsideAThreeLevelChainClosesTheTwoLevelsItIsOutsideOf()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var service = new RecordingPopupService(new SKRect(0f, 0f, 480f, 320f), serveWindows: true);
        OverridePopupService(scene, service);

        // Outer (20,50)-(220,200), middle (24,110)-(184,230), inner (28,134)-(108,174).
        var middleAnchor = new Button("Middle") { Bounds = new SKRect(4f, 30f, 124f, 60f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(middleAnchor);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 4f, 44f, 24f) };
        var middleContent = new Panel { Padding = Thickness.Empty };
        middleContent.Controls.Add(innerAnchor);

        var outerTask = anchor.ShowPopupAsync(outerContent, new PopupOptions { Size = new SKSize(200f, 150f) });
        var middleTask = middleAnchor.ShowPopupAsync(middleContent, new PopupOptions { Size = new SKSize(160f, 120f) });
        var innerTask = innerAnchor.ShowPopupAsync(new Panel(), new PopupOptions { Size = new SKSize(80f, 40f) });

        // Inside the outer popup and outside the two opened from it, so the walk goes through both of
        // them and stops at the level the press landed in.
        Assert.IsTrue(service.Windows[2].RaiseOutsidePress(new SKPoint(200f, 190f)));

        Assert.IsTrue(innerTask.IsCompletedSuccessfully, "the innermost level is outside it");
        Assert.IsTrue(middleTask.IsCompletedSuccessfully, "and so is the one below");
        Assert.AreEqual(PopupCloseReason.OutsideClick, innerTask.Result.Reason);
        Assert.AreEqual(PopupCloseReason.OutsideClick, middleTask.Result.Reason);
        Assert.IsFalse(outerTask.IsCompleted, "the walk stops at the level the press landed in");
        Assert.IsFalse(service.Windows[0].IsDisposed);
        Assert.IsTrue(service.Windows[1].IsDisposed);
        Assert.IsTrue(service.Windows[2].IsDisposed);
    }

    [TestMethod]
    public void APressOutsideAThreeLevelChainClosesAllThreeLevels()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var service = new RecordingPopupService(new SKRect(0f, 0f, 480f, 320f), serveWindows: true);
        OverridePopupService(scene, service);

        var middleAnchor = new Button("Middle") { Bounds = new SKRect(4f, 30f, 124f, 60f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(middleAnchor);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 4f, 44f, 24f) };
        var middleContent = new Panel { Padding = Thickness.Empty };
        middleContent.Controls.Add(innerAnchor);

        var outerTask = anchor.ShowPopupAsync(outerContent, new PopupOptions { Size = new SKSize(200f, 150f) });
        var middleTask = middleAnchor.ShowPopupAsync(middleContent, new PopupOptions { Size = new SKSize(160f, 120f) });
        var innerTask = innerAnchor.ShowPopupAsync(new Panel(), new PopupOptions { Size = new SKSize(80f, 40f) });

        Assert.IsTrue(service.Windows[2].RaiseOutsidePress(new SKPoint(450f, 300f)));

        Assert.IsTrue(innerTask.IsCompletedSuccessfully, "the innermost level is outside it");
        Assert.IsTrue(middleTask.IsCompletedSuccessfully, "and so is the one below");
        Assert.IsTrue(outerTask.IsCompletedSuccessfully, "and the one below that");
        Assert.AreEqual(3, service.Windows.Count(w => w.IsDisposed), "one press took the whole chain down");
        Assert.AreEqual(PopupCloseReason.OutsideClick, innerTask.Result.Reason);
        Assert.AreEqual(PopupCloseReason.OutsideClick, middleTask.Result.Reason);
        Assert.AreEqual(PopupCloseReason.OutsideClick, outerTask.Result.Reason);
    }

    [TestMethod]
    public void ALevelThatDoesNotDismissOnAnOutsidePressStopsTheWalk()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var service = new RecordingPopupService(new SKRect(0f, 0f, 480f, 320f), serveWindows: true);
        OverridePopupService(scene, service);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 30f, 124f, 60f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(innerAnchor);

        var outerTask = anchor.ShowPopupAsync(outerContent, new PopupOptions { Size = new SKSize(200f, 150f) });
        var innerTask = innerAnchor.ShowPopupAsync(new Panel(), new PopupOptions
        {
            Size = new SKSize(120f, 60f),
            DismissOnOutsideClick = false,
        });

        Assert.IsFalse(
            service.Windows[1].RaiseOutsidePress(new SKPoint(400f, 300f)),
            "nothing closed, so the press is not the popup's to take");

        Assert.IsFalse(innerTask.IsCompleted, "the level that opts out keeps itself open");
        Assert.IsFalse(outerTask.IsCompleted, "and the walk stops there rather than closing the popup above it");
        Assert.IsFalse(service.Windows[0].IsDisposed);
        Assert.IsFalse(service.Windows[1].IsDisposed);
    }

    [TestMethod]
    public void AnOptingOutParentIsLeftOpenWhenTheChildTakesThePress()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var service = new RecordingPopupService(new SKRect(0f, 0f, 480f, 320f), serveWindows: true);
        OverridePopupService(scene, service);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 30f, 124f, 60f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(innerAnchor);

        var outerTask = anchor.ShowPopupAsync(outerContent, new PopupOptions
        {
            Size = new SKSize(200f, 150f),
            DismissOnOutsideClick = false,
        });
        var innerTask = innerAnchor.ShowPopupAsync(new Panel(), new PopupOptions { Size = new SKSize(120f, 60f) });

        Assert.IsTrue(service.Windows[1].RaiseOutsidePress(new SKPoint(400f, 300f)), "the child closed");

        Assert.IsTrue(innerTask.IsCompletedSuccessfully, "the child is outside the press");
        Assert.AreEqual(PopupCloseReason.OutsideClick, innerTask.Result.Reason);
        Assert.IsFalse(outerTask.IsCompleted, "the level above opted out, so the walk stops at it");
        Assert.IsFalse(service.Windows[0].IsDisposed);
    }

    [TestMethod]
    public void EveryLevelAPressClosesIsReportedOnceWithOutsideClick()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var service = new RecordingPopupService(new SKRect(0f, 0f, 480f, 320f), serveWindows: true);
        OverridePopupService(scene, service);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 30f, 124f, 60f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(innerAnchor);

        var innerContent = new Panel();
        var outerReasons = new List<PopupCloseReason>();
        var innerReasons = new List<PopupCloseReason>();
        outerContent.AddPopupClosedHandler((_, result) => outerReasons.Add(result.Reason));
        innerContent.AddPopupClosedHandler((_, result) => innerReasons.Add(result.Reason));

        var outerTask = anchor.ShowPopupAsync(outerContent, new PopupOptions { Size = new SKSize(200f, 150f) });
        var innerTask = innerAnchor.ShowPopupAsync(innerContent, new PopupOptions { Size = new SKSize(120f, 60f) });

        Assert.IsTrue(service.Windows[1].RaiseOutsidePress(new SKPoint(400f, 300f)));
        scene.ClickAt(new SKPoint(400f, 300f));

        CollectionAssert.AreEqual(
            new[] { PopupCloseReason.OutsideClick },
            innerReasons,
            "the child was closed by the press, once, and a second press closes nothing");
        CollectionAssert.AreEqual(
            new[] { PopupCloseReason.OutsideClick },
            outerReasons,
            "and so was its parent");
        Assert.IsTrue(outerTask.IsCompletedSuccessfully);
        Assert.IsTrue(innerTask.IsCompletedSuccessfully);
    }

    // `IPopupWindow` is public - it is the advanced popup control seam - so `OutsidePress` is a
    // default interface member rather than a required one: a popup window written against an earlier
    // version of the interface keeps compiling, and keeps the dismissal it always had. What it does
    // not get is the chain rule, because the portable layer is never told where a press landed.
    // LegacyPopupWindow below is that shape on purpose: it never writes the member, so this test
    // failing to compile is itself the compatibility assertion.

    [TestMethod]
    public void APopupWindowThatDoesNotReportPressesKeepsClosingOneLevelPerDismissal()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var service = new LegacyPopupService();
        OverridePopupService(scene, service);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 30f, 124f, 60f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(innerAnchor);

        var outerTask = anchor.ShowPopupAsync(outerContent, new PopupOptions { Size = new SKSize(200f, 150f) });
        var innerTask = innerAnchor.ShowPopupAsync(new Panel(), new PopupOptions { Size = new SKSize(120f, 60f) });

        Assert.IsNull(
            ((IPopupWindow)service.Windows[1]).OutsidePress,
            "the default implementation reports nothing, so the assignment the popup layer makes is a no-op");

        service.Windows[1].RaiseDismissed();

        Assert.IsTrue(innerTask.IsCompletedSuccessfully, "the platform's own dismissal still closes the popup");
        Assert.AreEqual(PopupCloseReason.OutsideClick, innerTask.Result.Reason);
        Assert.IsFalse(outerTask.IsCompleted, "and it closes one level: the chain rule is what this backend does not get");

        service.Windows[0].RaiseDismissed();

        Assert.IsTrue(outerTask.IsCompletedSuccessfully, "the next dismissal takes the level above");
        Assert.AreEqual(PopupCloseReason.OutsideClick, outerTask.Result.Reason);
    }

    [TestMethod]
    public void ClosingAParentPopupWindowClosesThePopupOpenedInsideIt()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var service = new RecordingPopupService(new SKRect(0f, 0f, 480f, 320f), serveWindows: true);
        OverridePopupService(scene, service);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 30f, 124f, 60f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(innerAnchor);

        var outerTask = anchor.ShowPopupAsync(outerContent, new PopupOptions { Size = new SKSize(200f, 150f) });
        var innerTask = innerAnchor.ShowPopupAsync(new Panel(), new PopupOptions { Size = new SKSize(120f, 60f) });

        service.Windows[0].RaiseDismissed();

        Assert.IsTrue(outerTask.IsCompletedSuccessfully, "the platform took the outer popup away");
        Assert.IsTrue(innerTask.IsCompletedSuccessfully, "and the popup opened inside it went with it");
        Assert.AreEqual(PopupCloseReason.OwnerClosed, innerTask.Result.Reason);
        Assert.IsTrue(service.Windows[1].IsDisposed, "its surface is gone too");
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

    // The keyboard a popup borrows while it is open. No backend popup takes the focus, so the
    // window that has it forwards: these drive the window's own input entry points, which is what
    // every backend calls.

    [TestMethod]
    public void AKeyReachesTheFocusedControlInsideThePopup()
    {
        var (root, anchor) = CreateAnchorTree(new SKRect(20f, 20f, 120f, 50f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var spy = new KeySpy();
        var content = new Panel { Padding = Thickness.Empty };
        content.Controls.Add(spy);

        anchor.ShowPopupAsync(content, SizedPopup());

        Assert.IsTrue(spy.IsFocused, "opening the popup puts the keyboard into its own tree");
        Assert.AreSame(spy, scene.Host.FocusedControl);

        scene.Key(Key.A);

        CollectionAssert.AreEqual(new[] { Key.A }, spy.Keys.ToArray(), "the key arrives at that control");
    }

    [TestMethod]
    public void TypedTextReachesATextBoxInsideThePopup()
    {
        var (root, anchor) = CreateAnchorTree(new SKRect(20f, 20f, 120f, 50f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var textBox = new TextBox { Bounds = new SKRect(4f, 4f, 124f, 34f) };
        var content = new Panel { Padding = Thickness.Empty };
        content.Controls.Add(textBox);

        anchor.ShowPopupAsync(content, SizedPopup());

        Assert.IsTrue(textBox.IsFocused, "the text box is the first tab stop in the popup");

        scene.Type("Pop");

        Assert.AreEqual("Pop", textBox.Text, "what the user types lands in the field they can see");
    }

    [TestMethod]
    public void EscapeClosesThePopupAndCompletesWithEscape()
    {
        var (root, anchor) = CreateAnchorTree(new SKRect(20f, 20f, 120f, 50f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var content = new Panel { Padding = Thickness.Empty };
        content.Controls.Add(new KeySpy());

        var task = anchor.ShowPopupAsync(content, SizedPopup());

        scene.Key(Key.Escape);

        Assert.IsTrue(task.IsCompletedSuccessfully, "Escape is one of the five ways a popup ends");
        Assert.AreEqual(PopupCloseReason.Escape, task.Result.Reason);
        Assert.IsNull(content.Host, "and the popup is gone");
    }

    [TestMethod]
    public void EscapeLeavesThePopupOpenAndGoesNowhereWhenItDoesNotClose()
    {
        var (root, anchor) = CreateAnchorTree(new SKRect(20f, 20f, 120f, 50f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var ownerSpy = new KeySpy { Bounds = new SKRect(200f, 200f, 260f, 224f) };
        root.Controls.Add(ownerSpy);

        var popupSpy = new KeySpy();
        var content = new Panel { Padding = Thickness.Empty };
        content.Controls.Add(popupSpy);

        var task = anchor.ShowPopupAsync(content, new PopupOptions
        {
            Size = new SKSize(140f, 90f),
            EscapeCloses = false,
        });

        scene.Key(Key.Escape);

        Assert.IsFalse(task.IsCompleted, "the popup was told to ignore Escape");
        Assert.IsNotNull(content.Host, "so it is still showing");
        Assert.AreEqual(0, popupSpy.Keys.Count, "and the key is not handed to a control inside it");
        Assert.AreEqual(0, ownerSpy.Keys.Count, "nor to the control the window had focused");
    }

    [TestMethod]
    public void TabCyclesThePopupsOwnControlsAndNotTheWindowBehindIt()
    {
        var (root, anchor) = CreateAnchorTree(new SKRect(20f, 20f, 120f, 50f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var ownerSpy = new KeySpy { Bounds = new SKRect(200f, 200f, 260f, 224f) };
        root.Controls.Add(ownerSpy);

        var first = new KeySpy();
        var second = new KeySpy();
        var content = new Panel { Padding = Thickness.Empty };
        content.Controls.Add(first);
        content.Controls.Add(second);

        anchor.ShowPopupAsync(content, SizedPopup());

        Assert.IsTrue(first.IsFocused, "the popup starts on its first tab stop");

        scene.Key(Key.Tab);
        Assert.IsTrue(second.IsFocused, "Tab moves to the next control in the popup");
        Assert.IsFalse(ownerSpy.IsFocused, "and never to one behind it");

        scene.Key(Key.Tab);
        Assert.IsTrue(first.IsFocused, "Tab wraps round to the first control rather than leaving");

        scene.Key(Key.Tab, Modifiers.Shift);
        Assert.IsTrue(second.IsFocused, "Shift+Tab wraps the other way");
        Assert.IsTrue(scene.Host.FocusedControl is Control focused && (ReferenceEquals(focused, first) || ReferenceEquals(focused, second)),
            "the window's focus stays inside the popup throughout");
    }

    [TestMethod]
    public void ThePopupGivesTheKeyboardBackWhenItCloses()
    {
        var (root, anchor) = CreateAnchorTree(new SKRect(20f, 20f, 120f, 50f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var ownerSpy = new KeySpy { Bounds = new SKRect(200f, 200f, 260f, 224f) };
        root.Controls.Add(ownerSpy);
        ownerSpy.Focus();

        var popupSpy = new KeySpy();
        var content = new Panel { Padding = Thickness.Empty };
        content.Controls.Add(popupSpy);

        var task = anchor.ShowPopupAsync(content, SizedPopup());
        Assert.IsTrue(popupSpy.IsFocused, "the popup has the keyboard while it is open");

        content.ClosePopup();

        Assert.AreEqual(PopupCloseReason.Closed, task.Result.Reason);
        Assert.IsTrue(anchor.IsFocused, "the control that opened the popup takes the focus back");

        ownerSpy.Focus();
        scene.Key(Key.A);

        Assert.AreEqual(1, ownerSpy.Keys.Count, "the window's controls hear keys again");
        Assert.AreEqual(0, popupSpy.Keys.Count, "and the popup that closed hears nothing");
    }

    [TestMethod]
    public void APopupOpenedInsideAPopupRoutesKeysToTheInnermostOne()
    {
        var (root, anchor) = CreateAnchorTree(new SKRect(20f, 20f, 120f, 50f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var outerSpy = new KeySpy();
        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 40f, 124f, 70f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(outerSpy);
        outerContent.Controls.Add(innerAnchor);

        var outerTask = anchor.ShowPopupAsync(outerContent, SizedPopup());
        Assert.IsTrue(outerSpy.IsFocused, "the outer popup holds the keyboard first");

        var innerSpy = new KeySpy();
        var innerContent = new Panel { Padding = Thickness.Empty };
        innerContent.Controls.Add(innerSpy);

        var innerTask = innerAnchor.ShowPopupAsync(innerContent, SizedPopup());
        Assert.IsTrue(innerSpy.IsFocused, "the popup opened inside it takes the keyboard over");

        scene.Key(Key.A);

        Assert.AreEqual(1, innerSpy.Keys.Count, "the key goes to the innermost popup");
        Assert.AreEqual(0, outerSpy.Keys.Count, "and not to the one it sits in");

        scene.Key(Key.Escape);

        Assert.AreEqual(PopupCloseReason.Escape, innerTask.Result.Reason);
        Assert.IsFalse(outerTask.IsCompleted, "Escape unwinds one level at a time");

        scene.Key(Key.Escape);

        Assert.AreEqual(PopupCloseReason.Escape, outerTask.Result.Reason, "and the next press takes the level above");
    }

    // The release half of a keystroke. A Button and a CheckBox act on it, so a sink that forwarded
    // only presses left every control inside a popup untouchable from the keyboard.

    [TestMethod]
    public void TheReleaseOfAKeyFiresAButtonInsideThePopupExactlyOnce()
    {
        var (root, anchor) = CreateAnchorTree(new SKRect(20f, 20f, 120f, 50f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var button = new Button("Fire");
        var clicks = 0;
        button.AddClickHandler(_ => clicks++);

        var content = new Panel { Padding = Thickness.Empty };
        content.Controls.Add(button);

        anchor.ShowPopupAsync(content, SizedPopup());

        Assert.IsTrue(button.IsFocused, "the button is the popup's only tab stop");

        scene.Key(Key.Enter);

        Assert.IsTrue(button.IsPressed, "the press marks the button pressed, which is what it shows");
        Assert.AreEqual(0, clicks, "and a press on its own raises nothing");

        scene.KeyUp(Key.Enter);

        Assert.AreEqual(1, clicks, "the release is the click a keyboard activation raises");
        Assert.IsFalse(button.IsPressed, "and the pressed state is cleared with it");

        scene.TypeKey(Key.Space);

        Assert.AreEqual(2, clicks, "Space activates it the same way, once per keystroke");
        Assert.IsFalse(button.IsPressed);
    }

    [TestMethod]
    public void TheReleaseOfSpaceTogglesACheckBoxInsideThePopup()
    {
        var (root, anchor) = CreateAnchorTree(new SKRect(20f, 20f, 120f, 50f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var check = new CheckBox("Take it");
        var content = new Panel { Padding = Thickness.Empty };
        content.Controls.Add(check);

        anchor.ShowPopupAsync(content, SizedPopup());

        Assert.IsTrue(check.IsFocused, "the check box is the popup's first tab stop");

        scene.Key(Key.Space);

        Assert.IsTrue(check.IsPressed, "the press marks it pressed");
        Assert.IsFalse(check.Checked, "and a toggle flips on the release, as it does in the window");

        scene.KeyUp(Key.Space);

        Assert.IsTrue(check.Checked, "the release flips it once");
        Assert.IsFalse(check.IsPressed);

        scene.TypeKey(Key.Space);

        Assert.IsFalse(check.Checked, "and the next keystroke flips it back, once");
    }

    [TestMethod]
    public void AReleaseThePopupsControlDeclinesGoesOnToTheWindow()
    {
        var (root, anchor, _) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        // A window popup, so the window's own focus is a control outside the popup: that is what
        // makes "the release went on" observable rather than a second delivery to the same control.
        var service = new RecordingPopupService(new SKRect(0f, 0f, 480f, 320f), serveWindows: true);
        OverridePopupService(scene, service);

        var owner = new KeySpy(handlesKeys: false) { Bounds = new SKRect(240f, 240f, 340f, 270f) };
        root.Controls.Add(owner);
        owner.Focus();

        var popupSpy = new KeySpy(handlesKeys: false);
        var content = new Panel { Padding = Thickness.Empty };
        content.Controls.Add(popupSpy);

        anchor.ShowPopupAsync(content, SizedPopup());

        Assert.IsTrue(popupSpy.IsFocused, "the popup has the keyboard");
        Assert.AreSame(owner, scene.Host.FocusedControl, "and the window's own focus is still outside it");

        scene.TypeKey(Key.A);

        CollectionAssert.AreEqual(new[] { Key.A }, popupSpy.Keys, "the popup's control was offered the press once");
        CollectionAssert.AreEqual(new[] { Key.A }, popupSpy.Releases, "and the release once");
        CollectionAssert.AreEqual(new[] { Key.A }, owner.Keys.ToArray(), "the window hears the press the popup declined");
        CollectionAssert.AreEqual(new[] { Key.A }, owner.Releases.ToArray(), "and its release, so both halves behave alike");
    }

    [TestMethod]
    public void ASinkThatTakesTheOtherSinkAwayWhileHandlingAKeyStillAnswers()
    {
        var (root, _) = CreateAnchorTree(new SKRect(20f, 20f, 120f, 50f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var older = new SinkProbe(consumes: true);
        using var olderToken = scene.Host.AddInputSink(older);

        // The newest sink takes the older one off the host while it answers. The walk it was found
        // at is shorter by then, which is the case a popup closing on Escape produces.
        var newest = new SinkProbe(() =>
        {
            olderToken.Dispose();
            return true;
        });

        using var newestToken = scene.Host.AddInputSink(newest);

        Assert.IsTrue(scene.Host.ProcessKeyDown(Key.A), "the sink's answer is the press's answer");
        Assert.AreEqual(1, newest.KeyDownCalls, "the sink that took the key was asked once");
        Assert.AreEqual(0, older.KeyDownCalls, "and the one taken away was never reached");
    }

    [TestMethod]
    public void TheReleaseOfEscapeClosesNothingAndGoesNowhere()
    {
        var (root, anchor) = CreateAnchorTree(new SKRect(20f, 20f, 120f, 50f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var service = new RecordingPopupService(new SKRect(0f, 0f, 480f, 320f), serveWindows: true);
        OverridePopupService(scene, service);

        var owner = new KeySpy(handlesKeys: false);
        root.Controls.Add(owner);
        owner.Focus();

        var closed = new List<PopupResult>();
        var content = new Panel { Padding = Thickness.Empty };
        content.AddPopupClosedHandler((_, result) => closed.Add(result));
        content.Controls.Add(new KeySpy());

        var task = anchor.ShowPopupAsync(content, SizedPopup());

        scene.Key(Key.Escape);

        Assert.AreEqual(PopupCloseReason.Escape, task.Result.Reason, "the press closes it");
        Assert.AreEqual(1, closed.Count, "and reports that once");
        Assert.AreEqual(0, owner.Keys.Count, "the press is the popup's and stops there");

        scene.KeyUp(Key.Escape);

        Assert.AreEqual(1, closed.Count, "its release closes nothing a second time");
        Assert.AreEqual(0, owner.Releases.Count, "and does not reach the control behind it");

        scene.TypeKey(Key.Escape);

        Assert.AreEqual(0, owner.Keys.Count, "the same for a whole keystroke with no popup left");
    }

    [TestMethod]
    public void TheReleaseOfTheEscapeThatClosedTheInnerPopupLeavesTheOuterOneOpen()
    {
        var (root, anchor) = CreateAnchorTree(new SKRect(20f, 20f, 120f, 50f));
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var innerAnchor = new Button("Inner") { Bounds = new SKRect(4f, 40f, 124f, 70f) };
        var outerContent = new Panel { Padding = Thickness.Empty };
        outerContent.Controls.Add(innerAnchor);

        var outerTask = anchor.ShowPopupAsync(outerContent, SizedPopup());
        var innerTask = innerAnchor.ShowPopupAsync(new Panel { Padding = Thickness.Empty }, SizedPopup());

        scene.Key(Key.Escape);

        Assert.AreEqual(PopupCloseReason.Escape, innerTask.Result.Reason, "the press closes the innermost level");

        scene.KeyUp(Key.Escape);

        Assert.IsFalse(outerTask.IsCompleted, "and its release does not take the level above");

        scene.Key(Key.Escape);

        Assert.AreEqual(PopupCloseReason.Escape, outerTask.Result.Reason, "the next press does");
    }

    [TestMethod]
    public void EveryCloseRouteGivesTheKeyboardAndItsReleaseBack()
    {
        var (root, anchor, other) = CreatePopupTree();
        using var scene = HeadlessScene.Create(root, 480f, 320f);

        var owner = new KeySpy { Bounds = new SKRect(200f, 200f, 260f, 224f) };
        root.Controls.Add(owner);

        // Each route is applied to a fresh popup whose control took a whole keystroke, so the host
        // has a press of its own on record; afterwards the window must hear both halves of the next
        // one, which is what says no sink was left behind on any of the five paths.
        AssertSinkIsGone("application close", content => content.ClosePopup());
        AssertSinkIsGone("Escape", content => scene.TypeKey(Key.Escape));
        AssertSinkIsGone("outside press", content => scene.ClickAt(scene.CenterOf(other)));

        AssertSinkIsGone("replacement", content =>
        {
            var replacement = new Panel();
            anchor.ShowPopupAsync(replacement, SizedPopup());
            replacement.ClosePopup();
        });

        void AssertSinkIsGone(string route, Action<Panel> close)
        {
            var popupSpy = new KeySpy();
            var content = new Panel { Padding = Thickness.Empty };
            content.Controls.Add(popupSpy);

            var task = anchor.ShowPopupAsync(content, SizedPopup());

            scene.TypeKey(Key.A);

            Assert.AreEqual(1, popupSpy.Keys.Count, $"{route}: the popup took the press");
            Assert.AreEqual(1, popupSpy.Releases.Count, $"{route}: and its release");

            close(content);

            Assert.IsTrue(task.IsCompleted, $"{route}: the popup closed");

            owner.Focus();
            owner.Keys.Clear();
            owner.Releases.Clear();

            scene.TypeKey(Key.B);

            Assert.AreEqual(1, owner.Keys.Count, $"{route}: the window hears a press again");
            Assert.AreEqual(1, owner.Releases.Count, $"{route}: and its release");
            Assert.AreEqual(1, popupSpy.Keys.Count, $"{route}: the closed popup hears nothing");
            Assert.AreEqual(1, popupSpy.Releases.Count, $"{route}: in either phase");
        }
    }

    /// <summary>
    /// A sink with a counter, so a test can see the order the window's walk reached it in. It names
    /// no release handler: an implementer outside the library compiles unchanged, which is what the
    /// default interface member is for.
    /// </summary>
    private sealed class SinkProbe : IInputSink
    {
        private readonly Func<bool>? _onKeyDown;

        public SinkProbe(bool consumes)
            : this(() => consumes)
        {
        }

        public SinkProbe(Func<bool> onKeyDown) => _onKeyDown = onKeyDown;

        /// <summary>How many presses this sink was offered.</summary>
        public int KeyDownCalls { get; private set; }

        public bool ProcessKeyDown(Key key, Modifiers modifiers)
        {
            KeyDownCalls++;
            return _onKeyDown?.Invoke() ?? false;
        }

        public bool ProcessTextInput(string text) => false;
    }

    /// <summary>A control that records the keys and the typed text that reach it, and takes both.</summary>
    private sealed class KeySpy : Control
    {
        private readonly bool _handlesKeys;

        public KeySpy(bool handlesKeys = true)
        {
            _handlesKeys = handlesKeys;
            TabStop = true;
            MinimumSize = new SKSize(40f, 24f);
        }

        /// <summary>The keys this control was given, in order.</summary>
        public List<Key> Keys { get; } = new();

        /// <summary>The keys whose release this control was given, in order.</summary>
        public List<Key> Releases { get; } = new();

        /// <summary>Everything typed into this control, as it arrived.</summary>
        public string Typed { get; private set; } = string.Empty;

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            Keys.Add(e.Key);
            e.Handled = _handlesKeys;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            Releases.Add(e.Key);
            e.Handled = _handlesKeys;
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);
            Typed += e.Text;
        }
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

    /// <summary>A window with one button at <paramref name="bounds"/>, for measuring placement.</summary>
    private static (Panel Root, Button Anchor) CreateAnchorTree(SKRect bounds)
    {
        var anchor = new Button("Anchor") { Bounds = bounds };
        var root = new Panel { Dock = DockStyle.Fill, Padding = Thickness.Empty };

        root.Controls.Add(anchor);

        return (root, anchor);
    }

    /// <summary>Options with a fixed size, so placement is what a test measures rather than text.</summary>
    private static PopupOptions SizedPopup() => new() { Size = new SKSize(140f, 90f) };

    /// <summary>Puts <paramref name="popup"/> in front of the window's other services for this scene.</summary>
    private static void OverridePopupService(HeadlessScene scene, IPopupService popup) =>
        scene.Host.Services = new PopupServiceOverride(scene.Host.Services!, popup);

    /// <summary>The window's services, with <see cref="IPopupService"/> swapped for a test double.</summary>
    private sealed class PopupServiceOverride : IServiceProvider
    {
        private readonly IServiceProvider _inner;
        private readonly IPopupService _popup;

        public PopupServiceOverride(IServiceProvider inner, IPopupService popup)
        {
            _inner = inner;
            _popup = popup;
        }

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IPopupService) ? _popup : _inner.GetService(serviceType);
    }

    /// <summary>
    /// A popup service that answers the placement area with what it was given, and - when asked to -
    /// supplies a stand-in for a platform popup window so a test can see what the platform is handed.
    /// </summary>
    private sealed class RecordingPopupService : IPopupService
    {
        private readonly SKRect? _area;
        private readonly bool _serveWindows;

        public RecordingPopupService(SKRect? area, bool serveWindows = false)
        {
            _area = area;
            _serveWindows = serveWindows;
        }

        /// <summary>How many times the placement area was asked for.</summary>
        public int AreaQueries { get; private set; }

        /// <summary>The host the last area question was about.</summary>
        public UiHost? LastHost { get; private set; }

        /// <summary>The host the last <see cref="CreatePopup"/> question was about.</summary>
        public UiHost? LastCreatedFor { get; private set; }

        /// <summary>The surfaces handed out, in the order they were asked for.</summary>
        public List<RecordingPopupWindow> Windows { get; } = [];

        public IPopupWindow? CreatePopup(UiHost windowHost, bool dismissOnOutsideClick, bool takesPresses)
        {
            LastCreatedFor = windowHost;

            if (!_serveWindows)
            {
                return null;
            }

            var window = new RecordingPopupWindow(dismissOnOutsideClick, takesPresses);
            Windows.Add(window);
            return window;
        }

        public SKRect? GetPlacementArea(UiHost windowHost)
        {
            AreaQueries++;
            LastHost = windowHost;
            return _area;
        }
    }

    /// <summary>
    /// A stand-in for a platform popup window that records what it was shown with and can report that
    /// the platform dismissed it. It hosts the popup's content the way a real one does, so placement
    /// and layout inside the popup behave as they would on a display.
    /// </summary>
    private sealed class RecordingPopupWindow : IPopupWindow
    {
        private readonly UiHost _host = new();

        public RecordingPopupWindow(bool dismissesOnOutsideClick, bool takesPresses)
        {
            DismissesOnOutsideClick = dismissesOnOutsideClick;
            TakesPresses = takesPresses;
        }

        public UiHost Host => _host;

        public bool DismissesOnOutsideClick { get; }

        public bool TakesPresses { get; }

        /// <summary>What the popup layer decided a press outside this popup does.</summary>
        public Func<SKPoint, bool>? OutsidePress { get; set; }

        /// <summary>Host the popup was shown against.</summary>
        public UiHost? ShownFor { get; private set; }

        /// <summary>Where it was shown, in <see cref="ShownFor"/>'s coordinates.</summary>
        public SKPoint Location { get; private set; }

        /// <summary>The size it was shown at.</summary>
        public SKSize Size { get; private set; }

        /// <summary>Whether the popup layer has disposed this surface.</summary>
        public bool IsDisposed { get; private set; }

        public event Action<object>? Dismissed;

        public void Show(UiHost windowHost, SKPoint location, SKSize size)
        {
            ShownFor = windowHost;
            Location = location;
            Size = size;

            // What every backend does for the popup's own tree, and what makes the popup's content
            // measurable and placed: it resolves services from the window and draws at the size the
            // popup asked for.
            _host.Services = windowHost.Services;
            _host.SetClientSize(size.Width, size.Height);
        }

        public void UpdateBounds(SKPoint location, SKSize size)
        {
            Location = location;
            Size = size;
            _host.SetClientSize(size.Width, size.Height);
        }

        public void Hide()
        {
        }

        public void Dispose()
        {
            IsDisposed = true;
            _host.Root = null;
            Dismissed = null;
        }

        /// <summary>The platform closing the surface on the user's behalf.</summary>
        public void RaiseDismissed() => Dismissed?.Invoke(this);

        /// <summary>
        /// What a backend does with a press it saw on the owning window: hands it, with its point in
        /// that window's client coordinates, to the chain. Returns whether a level was closed.
        /// </summary>
        public bool RaiseOutsidePress(SKPoint point) => OutsidePress?.Invoke(point) ?? false;
    }

    /// <summary>
    /// A popup service that hands out <see cref="LegacyPopupWindow"/> surfaces: the shape a popup
    /// window written against an earlier version of <see cref="IPopupWindow"/> has, with no
    /// <c>OutsidePress</c> of its own.
    /// </summary>
    private sealed class LegacyPopupService : IPopupService
    {
        /// <summary>The surfaces handed out, in the order they were asked for.</summary>
        public List<LegacyPopupWindow> Windows { get; } = [];

        public IPopupWindow? CreatePopup(UiHost windowHost, bool dismissOnOutsideClick, bool takesPresses)
        {
            var window = new LegacyPopupWindow(dismissOnOutsideClick, takesPresses);
            Windows.Add(window);
            return window;
        }

        public SKRect? GetPlacementArea(UiHost windowHost) => new SKRect(0f, 0f, 480f, 320f);
    }

    /// <summary>
    /// A stand-in for a popup window written before <see cref="IPopupWindow.OutsidePress"/> existed:
    /// it implements the interface and never mentions the member, which only compiles because that
    /// member has a default implementation. It hosts and places the popup's content the way the
    /// recording stand-in does, so what it exercises is the interface's default and not a stub that
    /// would fail anyway.
    /// </summary>
    private sealed class LegacyPopupWindow : IPopupWindow
    {
        private readonly UiHost _host = new();

        public LegacyPopupWindow(bool dismissesOnOutsideClick, bool takesPresses)
        {
            DismissesOnOutsideClick = dismissesOnOutsideClick;
            TakesPresses = takesPresses;
        }

        public UiHost Host => _host;

        public bool DismissesOnOutsideClick { get; }

        public bool TakesPresses { get; }

        public event Action<object>? Dismissed;

        public void Show(UiHost windowHost, SKPoint location, SKSize size)
        {
            _host.Services = windowHost.Services;
            _host.SetClientSize(size.Width, size.Height);
        }

        public void UpdateBounds(SKPoint location, SKSize size)
        {
            _host.SetClientSize(size.Width, size.Height);
        }

        public void Hide()
        {
        }

        public void Dispose()
        {
            _host.Root = null;
            Dismissed = null;
        }

        /// <summary>The platform closing the surface on the user's behalf.</summary>
        public void RaiseDismissed() => Dismissed?.Invoke(this);
    }

    /// <summary>
    /// What an application's <c>await ShowPopupAsync(...)</c> becomes: it resumes under whatever
    /// context was current when it suspended, and reports the thread it resumed on.
    /// </summary>
    private static async Task AwaitPopupAsync(Task<PopupResult> popup, Action<PopupResult> onClosed) =>
        onClosed(await popup);
}

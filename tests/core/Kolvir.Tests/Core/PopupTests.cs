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

    [TestMethod]
    public void AnOutsidePressClosesTheInnermostPopupAndLeavesTheOneAboveIt()
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

        scene.ClickAt(new SKPoint(400f, 300f));

        Assert.IsTrue(innerTask.IsCompletedSuccessfully, "the innermost popup is the one the press belongs to");
        Assert.AreEqual(PopupCloseReason.OutsideClick, innerTask.Result.Reason);
        Assert.IsFalse(outerTask.IsCompleted, "the popup it was opened from is left alone");
        Assert.IsFalse(service.Windows[0].IsDisposed, "and its surface is still up");
        Assert.IsTrue(service.Windows[1].IsDisposed, "while the inner one's is gone");
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

    /// <summary>A control that records the keys and the typed text that reach it, and takes both.</summary>
    private sealed class KeySpy : Control
    {
        public KeySpy()
        {
            TabStop = true;
            MinimumSize = new SKSize(40f, 24f);
        }

        /// <summary>The keys this control was given, in order.</summary>
        public List<Key> Keys { get; } = new();

        /// <summary>Everything typed into this control, as it arrived.</summary>
        public string Typed { get; private set; } = string.Empty;

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            Keys.Add(e.Key);
            e.Handled = true;
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
    }

    /// <summary>
    /// What an application's <c>await ShowPopupAsync(...)</c> becomes: it resumes under whatever
    /// context was current when it suspended, and reports the thread it resumed on.
    /// </summary>
    private static async Task AwaitPopupAsync(Task<PopupResult> popup, Action<PopupResult> onClosed) =>
        onClosed(await popup);
}

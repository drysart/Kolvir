using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// <see cref="Control"/> itself: the contract every control inherits - parenting, enabled and
/// visible cascades, focus and tab traversal, events and their handler tokens, and layout
/// invalidation.
/// </summary>
/// <remarks>
/// These are the promises in CONTROLS-TODO.md section 2 rather than implementation details: a
/// control is "finished" when input is complete, disabled is complete and the events come in
/// Add/Remove pairs.
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class ControlModelTests : HeadlessTestBase
{
    [TestMethod]
    public void ANewControlHasTheDocumentedDefaults()
    {
        using var control = new Control();

        Assert.AreEqual(string.Empty, control.Text);
        Assert.AreEqual("Control", control.Name, "the default name is the type name");
        Assert.IsTrue(control.Visible);
        Assert.IsTrue(control.Enabled);
        Assert.IsTrue(control.Focusable);
        Assert.IsFalse(control.TabStop, "a plain control is not a tab stop");
        Assert.IsFalse(control.AutoSize);
        Assert.AreEqual(DockStyle.None, control.Dock);
        Assert.AreEqual(Cursor.Default, control.Cursor);
        Assert.AreEqual(0f, control.Padding.Left);
        Assert.AreEqual(0f, control.Margin.Left);
        Assert.AreEqual(SKSize.Empty, control.Size);
    }

    [TestMethod]
    public void AddingAChildSetsItsParentAndRemovingItClearsIt()
    {
        using var parent = new Panel();
        using var first = new Button("first");
        using var second = new Button("second");

        parent.Controls.Add(first);
        parent.Controls.Add(second);

        Assert.AreSame(parent, first.Parent, "adding a child should set its parent");
        Assert.AreEqual(2, parent.Controls.Count);

        parent.Controls.Remove(first);

        Assert.IsNull(first.Parent, "removing a child should clear its parent");
        Assert.AreEqual(1, parent.Controls.Count);
        Assert.AreSame(second, parent.Controls[0]);
    }

    [TestMethod]
    public void TheChildCollectionKeepsInsertionOrderAndSupportsInsert()
    {
        using var parent = new Panel();
        using var a = new Button("a");
        using var b = new Button("b");
        using var c = new Button("c");

        parent.Controls.Add(a);
        parent.Controls.Add(c);
        parent.Controls.Insert(1, b);

        CollectionAssert.AreEqual(new[] { a, b, c }, parent.Controls.ToArray());

        parent.Controls.RemoveAt(1);

        CollectionAssert.AreEqual(new[] { a, c }, parent.Controls.ToArray());
        Assert.IsNull(b.Parent);
    }

    [TestMethod]
    public void DisablingAnAncestorStopsInputReachingAChild()
    {
        var panel = PlainContainer();
        var button = new Button("Go") { Dock = DockStyle.Fill };
        panel.Controls.Add(button);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);
        int clicks = 0;
        button.AddClickHandler(_ => clicks++);

        scene.Click(button);
        Assert.AreEqual(1, clicks, "an enabled tree should deliver the click");

        panel.Enabled = false;
        scene.Click(button);

        Assert.AreEqual(1, clicks, "a disabled ancestor should stop the click reaching the child");
        Assert.IsFalse(button.IsFocused, "and should not leave the child focused");
    }

    [TestMethod]
    public void HidingAnAncestorStopsInputReachingAChild()
    {
        var panel = PlainContainer();
        var button = new Button("Go") { Dock = DockStyle.Fill };
        panel.Controls.Add(button);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);
        int clicks = 0;
        button.AddClickHandler(_ => clicks++);

        panel.Visible = false;
        scene.Click(button);

        Assert.AreEqual(0, clicks, "a hidden tree should not take input");
    }

    [TestMethod]
    public void FocusRaisesGotFocusAndLostFocus()
    {
        var first = new Button("first") { Dock = DockStyle.Top };
        var second = new Button("second") { Dock = DockStyle.Top };
        var panel = PlainContainer();
        panel.Controls.Add(first);
        panel.Controls.Add(second);

        using var scene = HeadlessScene.Create(panel, 200f, 120f);

        int firstFocus = 0, firstBlur = 0;
        first.AddGotFocusHandler(_ => firstFocus++);
        first.AddLostFocusHandler(_ => firstBlur++);

        first.Focus();

        Assert.AreSame(first, scene.Host.FocusedControl);
        Assert.IsTrue(first.IsFocused);
        Assert.AreEqual(1, firstFocus);

        second.Focus();

        Assert.AreSame(second, scene.Host.FocusedControl);
        Assert.IsFalse(first.IsFocused);
        Assert.AreEqual(1, firstBlur, "focus moving away should raise LostFocus once");
    }

    [TestMethod]
    public void TabMovesFocusInTreeOrderAndWraps()
    {
        var first = new Button("first") { Dock = DockStyle.Top };
        var second = new Button("second") { Dock = DockStyle.Top };
        var panel = PlainContainer();
        panel.Controls.Add(first);
        panel.Controls.Add(second);

        using var scene = HeadlessScene.Create(panel, 200f, 120f);

        first.Focus();
        scene.Key(Key.Tab);
        Assert.AreSame(second, scene.Host.FocusedControl, "Tab should move to the next tab stop");

        scene.Key(Key.Tab);
        Assert.AreSame(first, scene.Host.FocusedControl, "Tab at the last stop should wrap to the first");
    }

    [TestMethod]
    public void ShiftTabMovesFocusBackwards()
    {
        var first = new Button("first") { Dock = DockStyle.Top };
        var second = new Button("second") { Dock = DockStyle.Top };
        var panel = PlainContainer();
        panel.Controls.Add(first);
        panel.Controls.Add(second);

        using var scene = HeadlessScene.Create(panel, 200f, 120f);

        first.Focus();
        scene.Key(Key.Tab, Modifiers.Shift);

        Assert.AreSame(second, scene.Host.FocusedControl, "Shift+Tab from the first stop should wrap backwards");
    }

    [TestMethod]
    public void TabSkipsDisabledAndInvisibleControls()
    {
        var first = new Button("first") { Dock = DockStyle.Top };
        var skipped = new Button("skipped") { Dock = DockStyle.Top };
        var last = new Button("last") { Dock = DockStyle.Top };
        var panel = PlainContainer();
        panel.Controls.Add(first);
        panel.Controls.Add(skipped);
        panel.Controls.Add(last);

        using var scene = HeadlessScene.Create(panel, 200f, 160f);

        skipped.Enabled = false;
        first.Focus();
        scene.Key(Key.Tab);
        Assert.AreSame(last, scene.Host.FocusedControl, "a disabled control should not be a tab stop");

        skipped.Enabled = true;
        skipped.Visible = false;
        first.Focus();
        scene.Key(Key.Tab);
        Assert.AreSame(last, scene.Host.FocusedControl, "an invisible control should not be a tab stop");
    }

    [TestMethod]
    public void AControlThatIsNotATabStopIsNeverFocusedByTab()
    {
        var button = new Button("button") { Dock = DockStyle.Top };
        var plain = new Control { Dock = DockStyle.Top, Height = 30f };
        var panel = PlainContainer();
        panel.Controls.Add(button);
        panel.Controls.Add(plain);

        using var scene = HeadlessScene.Create(panel, 200f, 120f);

        button.Focus();
        scene.Key(Key.Tab);

        Assert.AreSame(button, scene.Host.FocusedControl, "the button is the only tab stop, so focus should wrap to it");
        Assert.IsFalse(plain.TabStop);
    }

    [TestMethod]
    public void TabFollowsTabIndexRatherThanTreeOrder()
    {
        // Created in the order third, second, first, so tree order and index order disagree.
        var third = new Button("third") { Dock = DockStyle.Top, TabIndex = 2 };
        var second = new Button("second") { Dock = DockStyle.Top, TabIndex = 1 };
        var first = new Button("first") { Dock = DockStyle.Top, TabIndex = 0 };
        var panel = PlainContainer();
        panel.Controls.Add(third);
        panel.Controls.Add(second);
        panel.Controls.Add(first);

        using var scene = HeadlessScene.Create(panel, 200f, 200f);

        third.Focus();
        Assert.AreSame(third, scene.Host.FocusedControl);

        scene.Key(Key.Tab);
        Assert.AreSame(first, scene.Host.FocusedControl, "index 0 should follow the last index once the order wraps");

        scene.Key(Key.Tab);
        Assert.AreSame(second, scene.Host.FocusedControl, "then index 1, not the child that comes next in the tree");

        scene.Key(Key.Tab);
        Assert.AreSame(third, scene.Host.FocusedControl, "and index 2 last");
    }

    [TestMethod]
    public void ShiftTabFollowsTabIndexBackwards()
    {
        // Created in the order third, second, first, so tree order and index order disagree.
        var third = new Button("third") { Dock = DockStyle.Top, TabIndex = 2 };
        var second = new Button("second") { Dock = DockStyle.Top, TabIndex = 1 };
        var first = new Button("first") { Dock = DockStyle.Top, TabIndex = 0 };
        var panel = PlainContainer();
        panel.Controls.Add(third);
        panel.Controls.Add(second);
        panel.Controls.Add(first);

        using var scene = HeadlessScene.Create(panel, 200f, 200f);

        third.Focus();
        scene.Key(Key.Tab, Modifiers.Shift);
        Assert.AreSame(second, scene.Host.FocusedControl, "Shift+Tab should walk the indices downwards, not the tree");

        scene.Key(Key.Tab, Modifiers.Shift);
        Assert.AreSame(first, scene.Host.FocusedControl);

        scene.Key(Key.Tab, Modifiers.Shift);
        Assert.AreSame(third, scene.Host.FocusedControl, "and wrap from the lowest index to the highest");
    }

    [TestMethod]
    public void EqualTabIndexValuesKeepTreeOrder()
    {
        var first = new Button("first") { Dock = DockStyle.Top, TabIndex = 1 };
        var second = new Button("second") { Dock = DockStyle.Top, TabIndex = 1 };
        var third = new Button("third") { Dock = DockStyle.Top, TabIndex = 1 };
        var panel = PlainContainer();
        panel.Controls.Add(first);
        panel.Controls.Add(second);
        panel.Controls.Add(third);

        using var scene = HeadlessScene.Create(panel, 200f, 200f);

        first.Focus();
        scene.Key(Key.Tab);
        Assert.AreSame(second, scene.Host.FocusedControl, "equal indices should keep the order the children were added in");

        scene.Key(Key.Tab);
        Assert.AreSame(third, scene.Host.FocusedControl, "and stay in that order to the end of the group");
    }

    [TestMethod]
    public void TabSkipsAControlThatIsNotATabStopEvenInTabIndexOrder()
    {
        // A(0), skipped(1), B(2), C(3) by index; A, C, B by tree order, so both the skip and the
        // order are needed to explain where focus lands.
        var last = new Button("last") { Dock = DockStyle.Top, TabIndex = 3 };
        var second = new Button("second") { Dock = DockStyle.Top, TabIndex = 2 };
        var skipped = new Button("skipped") { Dock = DockStyle.Top, TabIndex = 1, TabStop = false };
        var first = new Button("first") { Dock = DockStyle.Top, TabIndex = 0 };
        var panel = PlainContainer();
        panel.Controls.Add(first);
        panel.Controls.Add(skipped);
        panel.Controls.Add(last);
        panel.Controls.Add(second);

        using var scene = HeadlessScene.Create(panel, 200f, 240f);

        first.Focus();
        scene.Key(Key.Tab);
        Assert.AreSame(second, scene.Host.FocusedControl, "a control that is not a tab stop is skipped wherever its index puts it");

        scene.Key(Key.Tab);
        Assert.AreSame(last, scene.Host.FocusedControl, "and the stops after it follow their own indices");
        Assert.AreNotSame(skipped, scene.Host.FocusedControl);
    }

    [TestMethod]
    public void TabSkipsADisabledControlInTheMiddleOfTheIndexOrder()
    {
        // A(0), disabled(1), B(2), C(3) by index; A, C, B by tree order.
        var last = new Button("last") { Dock = DockStyle.Top, TabIndex = 3 };
        var second = new Button("second") { Dock = DockStyle.Top, TabIndex = 2 };
        var skipped = new Button("skipped") { Dock = DockStyle.Top, TabIndex = 1, Enabled = false };
        var first = new Button("first") { Dock = DockStyle.Top, TabIndex = 0 };
        var panel = PlainContainer();
        panel.Controls.Add(first);
        panel.Controls.Add(skipped);
        panel.Controls.Add(last);
        panel.Controls.Add(second);

        using var scene = HeadlessScene.Create(panel, 200f, 240f);

        first.Focus();
        scene.Key(Key.Tab);
        Assert.AreSame(second, scene.Host.FocusedControl, "a disabled control in the middle of the order is skipped");

        scene.Key(Key.Tab);
        Assert.AreSame(last, scene.Host.FocusedControl, "and the stops after it follow their own indices");
    }

    [TestMethod]
    public void ANestedContainerOrdersItsOwnChildrenAndIsEnteredAsAUnit()
    {
        // The outer order is first, panel, last; the inner order is innerSecond, innerFirst. The
        // inner indices are deliberately smaller than the outer ones: if the order were flat they
        // would sort against the outer controls instead of among themselves.
        var last = new Button("last") { Dock = DockStyle.Top, TabIndex = 9 };
        var innerSecond = new Button("innerSecond") { Dock = DockStyle.Top, TabIndex = 5 };
        var innerFirst = new Button("innerFirst") { Dock = DockStyle.Top, TabIndex = 1 };
        var nested = PlainContainer();
        nested.Dock = DockStyle.Top;
        nested.Height = 80f;
        nested.TabIndex = 1;
        nested.Controls.Add(innerSecond);
        nested.Controls.Add(innerFirst);

        var first = new Button("first") { Dock = DockStyle.Top, TabIndex = 0 };
        var panel = PlainContainer();
        panel.Controls.Add(last);
        panel.Controls.Add(nested);
        panel.Controls.Add(first);

        using var scene = HeadlessScene.Create(panel, 200f, 240f);

        first.Focus();
        scene.Key(Key.Tab);
        Assert.AreSame(innerFirst, scene.Host.FocusedControl, "a nested container should enter its own order, not the outer one");

        scene.Key(Key.Tab);
        Assert.AreSame(innerSecond, scene.Host.FocusedControl, "and finish its own order before leaving");

        scene.Key(Key.Tab);
        Assert.AreSame(last, scene.Host.FocusedControl, "then continue with the container's own next sibling");

        scene.Key(Key.Tab, Modifiers.Shift);
        Assert.AreSame(innerSecond, scene.Host.FocusedControl, "Shift+Tab should re-enter the container at its last stop");
    }

    [TestMethod]
    public void AFocusableContainerHasItsOwnPlaceInTheOrder()
    {
        // A split container is a tab stop that has children of its own: it is one step of the outer
        // order, and its children are visited straight after it rather than interleaved with it.
        var last = new Button("last") { Dock = DockStyle.Top, TabIndex = 2 };
        var container = new SplitContainer { TabIndex = 1, Height = 120f, Dock = DockStyle.Top };
        var inside = new Button("inside") { Dock = DockStyle.Top, TabIndex = 0 };
        container.Panel1.Controls.Add(inside);

        var first = new Button("first") { Dock = DockStyle.Top, TabIndex = 0 };
        var panel = PlainContainer();
        panel.Controls.Add(last);
        panel.Controls.Add(container);
        panel.Controls.Add(first);

        using var scene = HeadlessScene.Create(panel, 200f, 320f);

        first.Focus();
        scene.Key(Key.Tab);
        Assert.AreSame(container, scene.Host.FocusedControl, "a focusable container takes its own place in the order");

        scene.Key(Key.Tab);
        Assert.AreSame(inside, scene.Host.FocusedControl, "and its children come straight after it");

        scene.Key(Key.Tab);
        Assert.AreSame(last, scene.Host.FocusedControl, "before the container's next sibling");
    }

    [TestMethod]
    public void AHandlerTokenRemovesItsHandlerOnDispose()
    {
        var button = new Button("Go") { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(button, 200f, 80f);

        int clicks = 0;
        var token = button.AddClickHandler(_ => clicks++);

        scene.Click(button);
        Assert.AreEqual(1, clicks);

        token.Dispose();
        scene.Click(button);

        Assert.AreEqual(1, clicks, "a disposed token should have removed its handler");
    }

    [TestMethod]
    public void DisposingATokenTwiceDoesNothing()
    {
        var button = new Button("Go");
        var token = button.AddClickHandler(_ => { });

        token.Dispose();
        token.Dispose();
    }

    [TestMethod]
    public void TheRemoveMethodIsTheMirrorOfTheAddMethod()
    {
        var button = new Button("Go") { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(button, 200f, 80f);

        int clicks = 0;
        void OnClick(object _) => clicks++;

        button.AddClickHandler(OnClick);
        scene.Click(button);
        Assert.AreEqual(1, clicks);

        button.RemoveClickHandler(OnClick);
        scene.Click(button);
        Assert.AreEqual(1, clicks, "RemoveClickHandler should undo AddClickHandler");

        int moves = 0;
        void OnMove(object _, MouseEventArgs __) => moves++;

        button.AddMouseMoveHandler(OnMove);
        scene.MoveTo(button);
        Assert.AreEqual(1, moves);

        button.RemoveMouseMoveHandler(OnMove);
        scene.MoveTo(new SKPoint(1f, 1f));
        Assert.AreEqual(1, moves, "RemoveMouseMoveHandler should undo AddMouseMoveHandler");
    }

    [TestMethod]
    public void AssigningTheSameTextTwiceRaisesOneChange()
    {
        using var control = new Control();
        int changes = 0;
        control.AddTextChangedHandler(_ => changes++);

        control.Text = "hello";
        control.Text = "hello";

        Assert.AreEqual("hello", control.Text);
        Assert.AreEqual(1, changes, "an assignment that changes nothing should not raise TextChanged");
    }

    [TestMethod]
    public void SettingTheSizeRaisesBoundsChangedAndUpdatesTheGeometry()
    {
        using var control = new Control();
        int changes = 0;
        control.AddBoundsChangedHandler(_ => changes++);

        control.Size = new SKSize(60f, 20f);

        Assert.AreEqual(new SKSize(60f, 20f), control.Size);
        Assert.AreEqual(60f, control.Width);
        Assert.AreEqual(20f, control.Height);
        Assert.AreEqual(60f, control.Right);
        Assert.AreEqual(20f, control.Bottom);
        Assert.AreEqual(1, changes);

        control.Size = new SKSize(60f, 20f);

        Assert.AreEqual(1, changes, "an assignment that changes nothing should not raise BoundsChanged");
    }

    [TestMethod]
    public void VisibilityAndEnabledChangesRaiseTheirOwnEvents()
    {
        using var control = new Control();
        int visibility = 0, enabled = 0;
        control.AddVisibleChangedHandler(_ => visibility++);
        control.AddEnabledChangedHandler(_ => enabled++);

        control.Visible = false;
        control.Visible = false;
        control.Enabled = false;
        control.Enabled = false;

        Assert.AreEqual(1, visibility, "Visible should raise once for a real change");
        Assert.AreEqual(1, enabled, "Enabled should raise once for a real change");
    }

    [TestMethod]
    public void DisposingAContainerDisposesAndDetachesItsChildren()
    {
        var parent = new Panel();
        var grandchild = new Button("grandchild");
        var child = new Panel();
        child.Controls.Add(grandchild);
        parent.Controls.Add(child);

        parent.Dispose();

        Assert.AreEqual(0, parent.Controls.Count, "the parent should drop its children");
        Assert.AreEqual(0, child.Controls.Count, "and dispose them in turn");
        Assert.IsNull(child.Parent);
        Assert.IsNull(grandchild.Parent);
    }

    [TestMethod]
    public void HitTestFindsTheDeepestVisibleChild()
    {
        var panel = PlainContainer();
        var child = new Panel { Bounds = new SKRect(10f, 10f, 60f, 60f) };
        panel.Controls.Add(child);

        using var scene = HeadlessScene.Create(panel, 200f, 200f);

        Assert.AreSame(child, panel.HitTest(new SKPoint(20f, 20f)), "a point inside the child should find it");
        Assert.AreSame(panel, panel.HitTest(new SKPoint(150f, 150f)), "a point outside it should fall to the panel");
        Assert.IsNull(panel.HitTest(new SKPoint(-5f, -5f)), "a point outside the panel should find nothing");
    }

    [TestMethod]
    public void AHiddenChildIsNotHit()
    {
        var panel = PlainContainer();
        var child = new Panel { Bounds = new SKRect(10f, 10f, 60f, 60f), Visible = false };
        panel.Controls.Add(child);

        using var scene = HeadlessScene.Create(panel, 200f, 200f);

        Assert.AreSame(panel, panel.HitTest(new SKPoint(20f, 20f)), "a hidden child should not take the point");
    }

    [TestMethod]
    public void SuspendingLayoutDefersTheWorkUntilResume()
    {
        var panel = PlainContainer();
        var child = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(child);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        panel.SuspendLayout();
        panel.Size = new SKSize(300f, 150f);
        Assert.AreNotEqual(new SKSize(300f, 150f), child.Size, "layout should be deferred");

        panel.ResumeLayout();

        Assert.AreEqual(new SKSize(300f, 150f), child.Size, "ResumeLayout should run the deferred pass");
    }

    [TestMethod]
    public void ResumeLayoutWithoutSuspendIsAnError()
    {
        using var control = new Control();

        Assert.ThrowsExactly<InvalidOperationException>(() => control.ResumeLayout());
    }

    /// <summary>A container with no chrome, so a test's geometry assertions are about its children.</summary>
    private static Panel PlainContainer() => new()
    {
        Dock = DockStyle.Fill,
        ShowFrame = false,
        IsCard = false,
        BorderWidth = 0f,
        CornerRadius = 0f,
        Padding = Thickness.Empty,
    };
}

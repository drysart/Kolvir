using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The split button and its drop-down-only sibling: which half a press belongs to, the menu it
/// opens, and the keyboard that reaches both.
/// </summary>
/// <remarks>
/// The menu is opened through <see cref="MenuPresenter"/>, so on a headless machine it is
/// painted in the window's overlay layer. That is the fallback every application gets before a
/// backend registers a popup service, and it is enough to drive the whole chain - open, pick,
/// dismiss - without a window server.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class SplitButtonTests : HeadlessTestBase
{
    [TestMethod]
    public void TheMainAreaRunsTheDefaultAction()
    {
        var button = new SplitButton("Save") { Dock = DockStyle.Top, AutoSize = true };
        button.Menu.Add("Save as...");

        using var scene = Scene(button, 160f);

        int clicks = 0;
        button.AddClickHandler(_ => clicks++);

        scene.ClickAt(MainAreaPoint(button));

        Assert.AreEqual(
            1,
            clicks,
            $"pressing the main area runs the default action; bounds={button.Bounds} point={MainAreaPoint(button)} enabled={button.Enabled}");
        Assert.IsFalse(button.IsDropDownOpen, "and does not open the menu");
    }

    [TestMethod]
    public void TheArrowOpensTheMenuWithoutRunningTheDefaultAction()
    {
        var button = new SplitButton("Save") { Dock = DockStyle.Top, AutoSize = true };
        button.Menu.Add("Save as...");

        using var scene = Scene(button, 160f);

        int clicks = 0;
        button.AddClickHandler(_ => clicks++);

        scene.ClickAt(ArrowPoint(button));

        Assert.IsTrue(button.IsDropDownOpen, "the arrow opens the menu");
        Assert.AreEqual(0, clicks, "and the default action does not run");
        Assert.IsTrue(
            scene.Host.Overlays.OfType<MenuView>().Any(),
            "the open menu is hosted in the window's overlay layer");

        button.CloseDropDown();
    }

    [TestMethod]
    public void AReleaseOverTheOtherHalfDoesNothing()
    {
        var button = new SplitButton("Save") { Dock = DockStyle.Top, AutoSize = true };
        button.Menu.Add("Save as...");

        using var scene = Scene(button, 160f);

        int clicks = 0;
        button.AddClickHandler(_ => clicks++);

        scene.MoveTo(ArrowPoint(button));
        scene.Host.ProcessMouseDown(ArrowPoint(button), MouseButtons.Left, Modifiers.None);
        scene.Host.ProcessMouseUp(MainAreaPoint(button), MouseButtons.Left, Modifiers.None);

        Assert.AreEqual(0, clicks, "a press that slid off the half it started on is neither action");
        Assert.IsFalse(button.IsDropDownOpen);
    }

    [TestMethod]
    public void PickingARowRunsItsCommandAndClosesTheMenu()
    {
        var button = new SplitButton("Save") { Dock = DockStyle.Top, AutoSize = true };
        int picked = 0;
        button.Menu.Add("Save as...", () => picked++);
        button.Menu.Add("Export...");

        using var scene = Scene(button, 200f);

        button.OpenDropDown();

        var view = scene.Host.Overlays.OfType<MenuView>().Single();
        view.HighlightFirst();
        scene.TypeKey(Key.Enter);

        Assert.AreEqual(1, picked, "the highlighted row's command runs");
        Assert.IsFalse(button.IsDropDownOpen, "and the menu closes with the pick");
    }

    [TestMethod]
    public void ClickingOutsideClosesTheMenu()
    {
        var button = new SplitButton("Save") { Dock = DockStyle.Top, AutoSize = true };
        button.Menu.Add("Save as...");

        using var scene = Scene(button, 200f);

        button.OpenDropDown();
        Assert.IsTrue(button.IsDropDownOpen);

        scene.ClickAt(new SKPoint(10f, 180f));

        Assert.IsFalse(button.IsDropDownOpen, "a press outside the menu dismisses it");
    }

    [TestMethod]
    public void DownOpensTheMenuAndEscapeClosesIt()
    {
        var button = new SplitButton("Save") { Dock = DockStyle.Top, AutoSize = true };
        button.Menu.Add("Save as...");

        using var scene = Scene(button, 200f);

        button.Focus();
        scene.TypeKey(Key.Down);

        Assert.IsTrue(button.IsDropDownOpen, "Down opens the menu from the keyboard");

        scene.TypeKey(Key.Escape);

        Assert.IsFalse(button.IsDropDownOpen, "and Escape dismisses it");
    }

    [TestMethod]
    public void ADisabledButtonNeitherClicksNorOpensItsMenu()
    {
        var button = new SplitButton("Save") { Dock = DockStyle.Top, AutoSize = true, Enabled = false };
        button.Menu.Add("Save as...");

        using var scene = Scene(button, 160f);

        int clicks = 0;
        button.AddClickHandler(_ => clicks++);

        scene.ClickAt(MainAreaPoint(button));
        scene.ClickAt(ArrowPoint(button));
        button.OpenDropDown();

        Assert.AreEqual(0, clicks);
        Assert.IsFalse(button.IsDropDownOpen, "a disabled button takes no input");
    }

    [TestMethod]
    public void DisablingTheButtonClosesAnOpenMenu()
    {
        var button = new SplitButton("Save") { Dock = DockStyle.Top, AutoSize = true };
        button.Menu.Add("Save as...");

        using var scene = Scene(button, 200f);

        button.OpenDropDown();
        Assert.IsTrue(button.IsDropDownOpen);

        button.Enabled = false;

        Assert.IsFalse(button.IsDropDownOpen, "a disabled control cannot own an open menu");
    }

    [TestMethod]
    public void AnEmptyMenuDoesNotOpen()
    {
        var button = new SplitButton("Save") { Dock = DockStyle.Top, AutoSize = true };

        using var scene = Scene(button, 160f);

        scene.ClickAt(ArrowPoint(button));
        button.OpenDropDown();

        Assert.IsFalse(button.IsDropDownOpen, "there is nothing to show, so nothing opens");
    }

    [TestMethod]
    public void TheArrowIsPartOfTheMeasuredWidth()
    {
        using var plain = new Button("Save");
        using var split = new SplitButton("Save");

        float plainWidth = plain.Measure(new SKSize(400f, 400f)).Width;
        float splitWidth = split.Measure(new SKSize(400f, 400f)).Width;

        Assert.AreEqual(24f, splitWidth - plainWidth, "the arrow region is added to the label's own width");

        split.ArrowWidth = 40f;
        Assert.AreEqual(40f, split.Measure(new SKSize(400f, 400f)).Width - plainWidth);
    }

    [TestMethod]
    public void ADropDownButtonOpensItsMenuFromTheMainArea()
    {
        var button = new DropDownButton("More") { Dock = DockStyle.Top, AutoSize = true };
        button.Menu.Add("Export...");
        button.Menu.Add("Archive...");

        using var scene = Scene(button, 200f);

        int clicks = 0;
        button.AddClickHandler(_ => clicks++);

        scene.ClickAt(MainAreaPoint(button));

        Assert.IsTrue(button.IsDropDownOpen, "a drop-down button has no separate default action");
        Assert.AreEqual(1, clicks, "but its click still reaches a handler, as the WinForms control does");

        scene.ClickAt(new SKPoint(10f, 180f));
        Assert.IsFalse(button.IsDropDownOpen);
    }

    private static HeadlessScene Scene(Control button, float height)
    {
        // The root of a window is laid out at the client size, so a button put there directly would
        // be as tall as the window and its menu would open below the bottom of it. A plain panel
        // makes the button the size a page gives it.
        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = Thickness.Empty,
        };

        root.Controls.Add(button);
        return HeadlessScene.Create(root, 320f, height);
    }

    private static SKPoint MainAreaPoint(Control control)
    {
        var origin = control.GetPositionInHost();
        return new SKPoint(origin.X + 12f, origin.Y + (control.Height / 2f));
    }

    private static SKPoint ArrowPoint(SplitButton button)
    {
        var origin = button.GetPositionInHost();
        return new SKPoint(
            origin.X + button.Width - (button.ArrowWidth / 2f),
            origin.Y + (button.Height / 2f));
    }
}

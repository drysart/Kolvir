using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The in-library menu surface: the rows a menu paints, how the highlight moves, what Enter and
/// Escape do, and how a menu that does not fit scrolls.
/// </summary>
/// <remarks>
/// <see cref="MenuView"/> is a control and an <see cref="IInputSink"/>, which is what lets
/// a menu own the keyboard of the window it hangs off without being focused. These tests attach it
/// to a real host and drive the keys the host would forward.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class MenuViewTests : HeadlessTestBase
{
    [TestMethod]
    public void AMenuViewHasOneRowPerItem()
    {
        var first = new MenuItem("First");
        var separator = MenuItem.Separator;
        var second = new MenuItem("Second");

        using var view = new MenuView(new[] { first, separator, second });

        Assert.AreEqual(3, view.Items.Count);
        Assert.AreSame(separator, view.Items[1]);
        Assert.IsNull(view.HighlightedItem, "nothing is highlighted before the pointer or a key arrives");
        Assert.AreEqual(1, view.MenuChain.Count, "a menu with no submenu open is a chain of one");
        Assert.AreSame(view, view.DeepestMenu);
    }

    [TestMethod]
    public void TheFirstAndLastHighlightSkipTheSeparatorsAtTheEnds()
    {
        var first = new MenuItem("First");
        var middle = new MenuItem("Middle");
        var last = new MenuItem("Last");

        using var view = new MenuView(new[]
        {
            MenuItem.Separator,
            first,
            middle,
            last,
            MenuItem.Separator,
        });

        view.HighlightFirst();
        Assert.AreSame(first, view.HighlightedItem, "the leading separator is not a row the user can pick");

        view.HighlightLast();
        Assert.AreSame(last, view.HighlightedItem, "nor is the trailing one");
    }

    [TestMethod]
    public void TheNextHighlightSkipsADisabledRow()
    {
        var first = new MenuItem("First");
        var disabled = new MenuItem("Disabled") { Enabled = false };
        var last = new MenuItem("Last");

        using var view = new MenuView(new[] { first, disabled, last });

        view.HighlightFirst();
        view.HighlightNext();

        Assert.AreSame(last, view.HighlightedItem, "a disabled row is skipped");

        view.HighlightNext();
        Assert.AreSame(first, view.HighlightedItem, "and the run wraps at the end");
    }

    [TestMethod]
    public void TheArrowKeysMoveTheHighlightThroughTheHost()
    {
        var first = new MenuItem("First");
        var second = new MenuItem("Second");

        using var view = new MenuView(new[] { first, second });
        using var scene = HeadlessScene.Create(view, 240f, 200f);

        view.Attach(scene.Host);
        try
        {
            Assert.IsTrue(scene.Host.ProcessKeyDown(Key.Down), "the open menu should claim the arrow key");
            Assert.AreSame(first, view.HighlightedItem, "Down on nothing moves to the first row");

            scene.Key(Key.Down);
            Assert.AreSame(second, view.HighlightedItem);

            scene.Key(Key.Up);
            Assert.AreSame(first, view.HighlightedItem);
        }
        finally
        {
            view.Detach();
        }
    }

    [TestMethod]
    public void EnterPicksTheHighlightedRow()
    {
        var first = new MenuItem("First");
        var second = new MenuItem("Second");

        using var view = new MenuView(new[] { first, second });
        using var scene = HeadlessScene.Create(view, 240f, 200f);

        MenuItem? invoked = null;
        view.AddInvokedHandler((_, item) => invoked = item);

        view.Attach(scene.Host);
        try
        {
            view.HighlightFirst();
            scene.Key(Key.Enter);
        }
        finally
        {
            view.Detach();
        }

        Assert.AreSame(first, invoked, "the menu should report the row the user picked");
    }

    [TestMethod]
    public void ADisabledRowCannotBeActivated()
    {
        var disabled = new MenuItem("Disabled") { Enabled = false };

        using var view = new MenuView(new[] { disabled });
        using var scene = HeadlessScene.Create(view, 240f, 200f);

        int invoked = 0;
        view.AddInvokedHandler((_, _) => invoked++);

        Assert.IsFalse(view.Activate(0), "a disabled row is not pickable");
        Assert.AreEqual(0, invoked);
    }

    [TestMethod]
    public void EscapeAsksTheMenuToDismiss()
    {
        using var view = new MenuView(new[] { new MenuItem("Only") });
        using var scene = HeadlessScene.Create(view, 240f, 200f);

        int dismissed = 0;
        view.AddDismissedHandler(_ => dismissed++);

        Assert.IsTrue(view.ProcessKeyDown(Key.Escape, Modifiers.None), "Escape belongs to the menu");
        Assert.AreEqual(1, dismissed);
    }

    [TestMethod]
    public void ALongMenuScrollsToKeepTheHighlightInView()
    {
        var items = new MenuItem[60];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = new MenuItem($"Row {i}");
        }

        using var view = new MenuView(items) { MaximumSurfaceHeight = 160f };
        using var scene = HeadlessScene.Create(view, 300f, 300f);

        view.HighlightFirst();
        Assert.AreEqual(0f, view.ScrollOffset, "the first row is already at the top");

        view.HighlightLast();

        Assert.IsTrue(view.ScrollOffset > 0f, "reaching the last row should scroll the menu");
        Assert.AreSame(items[^1], view.HighlightedItem);

        view.ScrollTo(100_000f);
        float bottom = view.ScrollOffset;
        Assert.IsTrue(bottom > 0f);

        view.ScrollTo(-50f);
        Assert.AreEqual(0f, view.ScrollOffset, "an offset past the start clamps to it");
    }

    [TestMethod]
    public void AMenuOpenedFromAContextMenuHasTheSameRows()
    {
        var menu = new ContextMenu().Add("One").AddSeparator().Add("Two", () => { });

        using var view = new MenuView(menu);

        Assert.AreEqual(3, view.Items.Count);
        Assert.AreSame(menu.Items[0], view.Items[0]);
        Assert.AreSame(menu.Items[2], view.Items[2]);

        view.SetItems(new[] { new MenuItem("Replaced") });
        Assert.AreEqual(1, view.Items.Count, "SetItems replaces the rows");
    }

    [TestMethod]
    public void TheLayoutPlacesOneRowPerItemFromTheTopDown()
    {
        var first = new MenuItem("First");
        var disabled = new MenuItem("Disabled") { Enabled = false };
        var last = new MenuItem("Last");

        using var view = new MenuView(new[]
        {
            first,
            MenuItem.Separator,
            disabled,
            last,
        });
        using var scene = HeadlessScene.Create(view, 240f, 220f);

        view.HighlightFirst();
        scene.Render();

        var layout = view.Layout;
        Assert.IsNotNull(layout, "measuring a menu produces a layout");
        Assert.AreEqual(4, layout.Rows.Count, "one row per item, separators included");

        float previousBottom = float.NegativeInfinity;
        for (int i = 0; i < layout.Rows.Count; i++)
        {
            var row = layout.Rows[i];
            Assert.IsTrue(row.Bounds.Height > 0f, $"row {i} has height");
            Assert.IsTrue(row.Bounds.Top >= previousBottom - 0.01f, $"row {i} starts below the one before it");
            Assert.IsTrue(
                row.Bounds.Left >= layout.SurfaceBounds.Left - 0.01f &&
                row.Bounds.Right <= layout.SurfaceBounds.Right + 0.01f,
                $"row {i} stays inside the menu's surface");
            previousBottom = row.Bounds.Bottom;
        }

        Assert.IsTrue(layout.Rows[0].IsPickable, "an ordinary enabled item is pickable");
        Assert.IsFalse(layout.Rows[1].IsPickable, "a separator is not");
        Assert.IsFalse(layout.Rows[2].IsPickable, "a disabled item is not");
        Assert.IsTrue(layout.Rows[3].IsPickable);
        Assert.AreEqual(0, layout.FirstPickable, "the first pickable row is the first item here");
    }

    [TestMethod]
    public void AMenuReportsWhetherItScrolls()
    {
        var few = new MenuView(new[] { new MenuItem("One"), new MenuItem("Two") });
        using (few)
        using (var scene = HeadlessScene.Create(few, 240f, 220f))
        {
            few.HighlightFirst();
            scene.Render();

            Assert.IsNotNull(few.Layout);
            Assert.IsFalse(few.Layout.IsScrollable, "a menu that fits does not scroll");
            Assert.AreEqual(0f, few.Layout.MaxScroll);
        }

        var many = new MenuItem[40];
        for (int i = 0; i < many.Length; i++)
        {
            many[i] = new MenuItem($"Row {i}");
        }

        var longMenu = new MenuView(many) { MaximumSurfaceHeight = 120f };
        using (longMenu)
        using (var scene = HeadlessScene.Create(longMenu, 300f, 300f))
        {
            longMenu.HighlightFirst();
            scene.Render();

            Assert.IsNotNull(longMenu.Layout);
            Assert.IsTrue(longMenu.Layout.IsScrollable, "a menu taller than its maximum surface scrolls");
            Assert.IsTrue(longMenu.Layout.MaxScroll > 0f);
            Assert.IsTrue(
                longMenu.Layout.ContentHeight > longMenu.Layout.SurfaceSize.Height,
                "its content is taller than the surface it is shown in");
        }
    }
}

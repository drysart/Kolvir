using SkiaSharp;
using XPlatUI;

namespace XPlatUI.Tests;

/// <summary>
/// The in-library menu surface: the rows a menu paints, how the highlight moves, what Enter and
/// Escape do, and how a menu that does not fit scrolls.
/// </summary>
/// <remarks>
/// <see cref="XPlatMenuView"/> is a control and an <see cref="IXPlatInputSink"/>, which is what lets
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
        var first = new XPlatMenuItem("First");
        var separator = XPlatMenuItem.Separator;
        var second = new XPlatMenuItem("Second");

        using var view = new XPlatMenuView(new[] { first, separator, second });

        Assert.AreEqual(3, view.Items.Count);
        Assert.AreSame(separator, view.Items[1]);
        Assert.IsNull(view.HighlightedItem, "nothing is highlighted before the pointer or a key arrives");
        Assert.AreEqual(1, view.MenuChain.Count, "a menu with no submenu open is a chain of one");
        Assert.AreSame(view, view.DeepestMenu);
    }

    [TestMethod]
    public void TheFirstAndLastHighlightSkipTheSeparatorsAtTheEnds()
    {
        var first = new XPlatMenuItem("First");
        var middle = new XPlatMenuItem("Middle");
        var last = new XPlatMenuItem("Last");

        using var view = new XPlatMenuView(new[]
        {
            XPlatMenuItem.Separator,
            first,
            middle,
            last,
            XPlatMenuItem.Separator,
        });

        view.HighlightFirst();
        Assert.AreSame(first, view.HighlightedItem, "the leading separator is not a row the user can pick");

        view.HighlightLast();
        Assert.AreSame(last, view.HighlightedItem, "nor is the trailing one");
    }

    [TestMethod]
    public void TheNextHighlightSkipsADisabledRow()
    {
        var first = new XPlatMenuItem("First");
        var disabled = new XPlatMenuItem("Disabled") { Enabled = false };
        var last = new XPlatMenuItem("Last");

        using var view = new XPlatMenuView(new[] { first, disabled, last });

        view.HighlightFirst();
        view.HighlightNext();

        Assert.AreSame(last, view.HighlightedItem, "a disabled row is skipped");

        view.HighlightNext();
        Assert.AreSame(first, view.HighlightedItem, "and the run wraps at the end");
    }

    [TestMethod]
    public void TheArrowKeysMoveTheHighlightThroughTheHost()
    {
        var first = new XPlatMenuItem("First");
        var second = new XPlatMenuItem("Second");

        using var view = new XPlatMenuView(new[] { first, second });
        using var scene = HeadlessScene.Create(view, 240f, 200f);

        view.Attach(scene.Host);
        try
        {
            Assert.IsTrue(scene.Host.ProcessKeyDown(XPlatKey.Down), "the open menu should claim the arrow key");
            Assert.AreSame(first, view.HighlightedItem, "Down on nothing moves to the first row");

            scene.Key(XPlatKey.Down);
            Assert.AreSame(second, view.HighlightedItem);

            scene.Key(XPlatKey.Up);
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
        var first = new XPlatMenuItem("First");
        var second = new XPlatMenuItem("Second");

        using var view = new XPlatMenuView(new[] { first, second });
        using var scene = HeadlessScene.Create(view, 240f, 200f);

        XPlatMenuItem? invoked = null;
        view.AddInvokedHandler((_, item) => invoked = item);

        view.Attach(scene.Host);
        try
        {
            view.HighlightFirst();
            scene.Key(XPlatKey.Enter);
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
        var disabled = new XPlatMenuItem("Disabled") { Enabled = false };

        using var view = new XPlatMenuView(new[] { disabled });
        using var scene = HeadlessScene.Create(view, 240f, 200f);

        int invoked = 0;
        view.AddInvokedHandler((_, _) => invoked++);

        Assert.IsFalse(view.Activate(0), "a disabled row is not pickable");
        Assert.AreEqual(0, invoked);
    }

    [TestMethod]
    public void EscapeAsksTheMenuToDismiss()
    {
        using var view = new XPlatMenuView(new[] { new XPlatMenuItem("Only") });
        using var scene = HeadlessScene.Create(view, 240f, 200f);

        int dismissed = 0;
        view.AddDismissedHandler(_ => dismissed++);

        Assert.IsTrue(view.ProcessKeyDown(XPlatKey.Escape, XPlatModifiers.None), "Escape belongs to the menu");
        Assert.AreEqual(1, dismissed);
    }

    [TestMethod]
    public void ALongMenuScrollsToKeepTheHighlightInView()
    {
        var items = new XPlatMenuItem[60];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = new XPlatMenuItem($"Row {i}");
        }

        using var view = new XPlatMenuView(items) { MaximumSurfaceHeight = 160f };
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
        var menu = new XPlatContextMenu().Add("One").AddSeparator().Add("Two", () => { });

        using var view = new XPlatMenuView(menu);

        Assert.AreEqual(3, view.Items.Count);
        Assert.AreSame(menu.Items[0], view.Items[0]);
        Assert.AreSame(menu.Items[2], view.Items[2]);

        view.SetItems(new[] { new XPlatMenuItem("Replaced") });
        Assert.AreEqual(1, view.Items.Count, "SetItems replaces the rows");
    }

    [TestMethod]
    public void TheLayoutPlacesOneRowPerItemFromTheTopDown()
    {
        var first = new XPlatMenuItem("First");
        var disabled = new XPlatMenuItem("Disabled") { Enabled = false };
        var last = new XPlatMenuItem("Last");

        using var view = new XPlatMenuView(new[]
        {
            first,
            XPlatMenuItem.Separator,
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
        var few = new XPlatMenuView(new[] { new XPlatMenuItem("One"), new XPlatMenuItem("Two") });
        using (few)
        using (var scene = HeadlessScene.Create(few, 240f, 220f))
        {
            few.HighlightFirst();
            scene.Render();

            Assert.IsNotNull(few.Layout);
            Assert.IsFalse(few.Layout.IsScrollable, "a menu that fits does not scroll");
            Assert.AreEqual(0f, few.Layout.MaxScroll);
        }

        var many = new XPlatMenuItem[40];
        for (int i = 0; i < many.Length; i++)
        {
            many[i] = new XPlatMenuItem($"Row {i}");
        }

        var longMenu = new XPlatMenuView(many) { MaximumSurfaceHeight = 120f };
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

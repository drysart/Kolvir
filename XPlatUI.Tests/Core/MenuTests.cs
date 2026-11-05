using SkiaSharp;
using XPlatUI;

namespace XPlatUI.Tests;

/// <summary>
/// The portable menu model: the rows a context menu holds, mnemonics, shortcuts, submenus, and what
/// a click on a row does.
/// </summary>
/// <remarks>
/// The presenter and the popup that draws a menu are a platform seam; what an application actually
/// builds and hands to that seam is this model, and it is the part with rules worth pinning.
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class MenuTests : HeadlessTestBase
{
    [TestMethod]
    public void AContextMenuKeepsItsRowsInOrderAndSpellsASeparator()
    {
        var menu = new XPlatContextMenu()
            .Add("Cut")
            .AddSeparator()
            .Add("Paste");

        Assert.AreEqual(3, menu.Items.Count);
        Assert.AreEqual("Cut", menu.Items[0].Text);
        Assert.IsTrue(menu.Items[1].IsSeparator, "the middle row is a separator");
        Assert.AreEqual("Paste", menu.Items[2].Text);
        Assert.IsFalse(menu.IsEmpty);
    }

    [TestMethod]
    public void InvokingARowRunsItsCommandOnce()
    {
        int runs = 0;
        var menu = new XPlatContextMenu().Add("Delete", () => runs++);

        Assert.IsTrue(menu.Items[0].Invoke());
        Assert.AreEqual(1, runs);

        // A row that is switched off must not run anything, which is what a disabled menu row does.
        menu.Items[0].Enabled = false;
        Assert.IsFalse(menu.Items[0].Invoke());
        Assert.AreEqual(1, runs, "a disabled row should swallow the click");
    }

    [TestMethod]
    public void ARowWithSubItemsIsASubmenu()
    {
        var parent = new XPlatMenuItem("&Theme");
        parent.SubItems.Add(new XPlatMenuItem("&Dark"));
        parent.SubItems.Add(new XPlatMenuItem("&Light"));

        Assert.IsTrue(parent.HasSubMenu);
        Assert.AreEqual(2, parent.SubItems.Count);

        var leaf = new XPlatMenuItem("&Dark");
        Assert.IsFalse(leaf.HasSubMenu, "a plain row is not a submenu");
    }

    [TestMethod]
    public void AMnemonicIsTheAmpersandFollowedCharacter()
    {
        var item = new XPlatMenuItem("&Save");

        Assert.AreEqual('S', item.Mnemonic);
        Assert.AreEqual("Save", item.DisplayText, "the display text drops the ampersand");
    }

    [TestMethod]
    public void AShortcutRendersItsDisplayText()
    {
        var item = new XPlatMenuItem("&Save") { Shortcut = new XPlatShortcut(XPlatKey.S, XPlatModifiers.Control) };

        string expected = OperatingSystem.IsMacOS() ? "Cmd+S" : "Ctrl+S";
        Assert.AreEqual(expected, item.ShortcutDisplayText, "the accelerator modifier is spelled per platform");

        item.ShortcutText = "F5";
        Assert.AreEqual("F5", item.ShortcutDisplayText, "explicit text wins over the shortcut");
    }

    [TestMethod]
    public void AShortcutRequiresAKeyAndAModifier()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new XPlatShortcut(XPlatKey.None));
        Assert.ThrowsExactly<ArgumentException>(() => new XPlatShortcut(XPlatKey.A, XPlatModifiers.None));
    }

    [TestMethod]
    public void InsertingPutsARowAtTheRequestedPosition()
    {
        var menu = new XPlatContextMenu().Add("First").Add("Third");

        menu.Insert(1, new XPlatMenuItem("Second"));

        Assert.AreEqual("Second", menu.Items[1].Text);
        Assert.AreEqual(3, menu.Items.Count);

        menu.Clear();
        Assert.AreEqual(0, menu.Items.Count);
        Assert.IsTrue(menu.IsEmpty);
    }

    [TestMethod]
    public void AContextMenuOpeningHandlerSeesTheControlsMenuAndCanReplaceIt()
    {
        var button = new XPlatButton("Go") { Dock = DockStyle.Fill };
        button.ContextMenu = new XPlatContextMenu().Add("Own row");

        using var scene = HeadlessScene.Create(button, 200f, 80f);
        XPlatContextMenu? seen = null;
        XPlatContextMenu? replacement = null;
        XPlatContextMenuEventArgs? captured = null;

        using (button.AddContextMenuOpeningHandler(args =>
        {
            captured = args;
            seen = args.Menu;
            replacement = new XPlatContextMenu().Add("Replaced");
            args.Menu = replacement;
        }))
        {
            scene.Host.ProcessContextMenu(new SKPoint(20f, 20f));
        }

        Assert.IsNotNull(seen, "the control's own menu should reach the handler");
        Assert.AreEqual("Own row", seen!.Items[0].Text);
        Assert.IsNotNull(captured);
        Assert.AreSame(replacement, captured!.Menu, "the handler's replacement is what the presenter shows");
    }
}

using AppKit;
using CoreGraphics;

namespace Kolvir.Backend.AppKit;

/// <summary>
/// Shows <see cref="ContextMenu"/> values with a native NSMenu, so a right-click gets the same
/// menu every other app on the machine gets - including the system's own appearance, vibrancy and
/// keyboard navigation. This is the counterpart of the Win32 and GTK presenters: the library only
/// describes the menu, the platform layer presents it.
/// </summary>
internal sealed class AppKitContextMenuPresenter : IContextMenuPresenter
{
    private readonly NSView _view;

    public AppKitContextMenuPresenter(NSView view) => _view = view;

    public void Show(ContextMenuRequest request)
    {
        var items = request.Menu.TrimmedItems;
        if (items.Count == 0)
        {
            return;
        }

        var menu = new NSMenu("Context")
        {
            // Without this AppKit enables or disables items by asking the responder chain, which
            // would override the enabled state the library has already decided on.
            AutoEnablesItems = false,
        };

        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                menu.AddItem(NSMenuItem.SeparatorItem);
                continue;
            }

            menu.AddItem(CreateItem(item));
        }

        // The location is in the view's (flipped) coordinate space, which is exactly the space
        // popUpMenuPositioningItem:atLocation:inView: expects, so no conversion is needed.
        var location = new CGPoint(request.Location.X, request.Location.Y);
        menu.PopUpMenu(null!, location, _view);
    }

    internal static NSMenuItem CreateItem(MenuItem item)
    {
        var menuItem = new NSMenuItem(item.DisplayText, string.Empty, (_, _) => item.Invoke())
        {
            Enabled = item.Enabled,
            State = item.Checked ? NSCellStateValue.On : NSCellStateValue.Off,
        };

        ApplyShortcut(menuItem, item);

        if (item.HasSubMenu)
        {
            // A row that opens a submenu runs nothing itself: AppKit takes the click and shows the
            // menu attached to it, so the action above is never reached.
            var submenu = new NSMenu(item.DisplayText) { AutoEnablesItems = false };
            AddItems(submenu, item.TrimmedSubItems);
            menuItem.Submenu = submenu;
        }

        return menuItem;
    }

    /// <summary>
    /// Fills <paramref name="menu"/> with <paramref name="items"/>, at any depth: an item that
    /// carries <see cref="MenuItem.SubItems"/> becomes a real <c>NSMenu</c> submenu.
    /// </summary>
    internal static void AddItems(NSMenu menu, IEnumerable<MenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                menu.AddItem(NSMenuItem.SeparatorItem);
                continue;
            }

            menu.AddItem(CreateItem(item));
        }
    }

    /// <summary>
    /// Gives a menu item its native key equivalent, so the shortcut shows as the Command key glyph
    /// every other macOS menu uses and AppKit dispatches it before the view sees the key.
    /// </summary>
    /// <remarks>
    /// A hint that was set as text alone stays a hint: the library cannot know which key "Ctrl+Shift+F"
    /// means without a <see cref="Shortcut"/>, and guessing from the last character produced a
    /// key equivalent that ignored the other modifiers.
    /// </remarks>
    internal static void ApplyShortcut(NSMenuItem menuItem, MenuItem item)
    {
        if (item.Shortcut is not { } shortcut ||
            AppKitKeyMap.ToKeyEquivalent(shortcut.Key) is not { } keyEquivalent)
        {
            return;
        }

        menuItem.KeyEquivalent = keyEquivalent;
        menuItem.KeyEquivalentModifierMask = AppKitKeyMap.ToKeyEquivalentMask(shortcut.Modifiers);
    }
}
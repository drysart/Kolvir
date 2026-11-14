namespace Kolvir.Backend.Gtk4;

/// <summary>
/// Builds the GTK widgets for <see cref="MenuItem"/> values. Context menus and the menu bar
/// share this so the two cannot drift apart visually or in behaviour.
/// </summary>
internal static class Gtk4MenuItemWidget
{
    /// <summary>
    /// The tallest a menu may grow before it scrolls. A menu here is a widget in a popover rather
    /// than a platform menu, so nothing else would stop a menu with hundreds of rows from running
    /// off the bottom of the display.
    /// </summary>
    public const int MaxMenuHeight = 480;

    /// <summary>
    /// Builds the body of a menu: one row per item, in a box <paramref name="width"/> wide. A
    /// submenu is the same box in its own popover, so the nesting is the model's.
    /// </summary>
    /// <param name="items">The items to build rows for, in order.</param>
    /// <param name="beforeInvoke">Runs before an item is invoked, such as to close the menu.</param>
    /// <param name="width">The width each row is built in, in pixels.</param>
    /// <param name="maxHeight">
    /// The tallest the list may get before it scrolls, or 0 to let it grow as tall as its rows are.
    /// </param>
    public static Gtk.Widget CreateList(
        IEnumerable<MenuItem> items,
        Action? beforeInvoke,
        int width = 200,
        int maxHeight = MaxMenuHeight)
    {
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        box.SetSizeRequest(width, -1);
        box.SetMarginTop(4);
        box.SetMarginBottom(4);

        foreach (var item in items)
        {
            box.Append(item.IsSeparator
                ? CreateSeparator()
                : Create(item, beforeInvoke, width));
        }

        if (maxHeight <= 0)
        {
            return box;
        }

        var scroller = Gtk.ScrolledWindow.New();
        scroller.SetPolicy(Gtk.PolicyType.Never, Gtk.PolicyType.Automatic);
        scroller.SetPropagateNaturalHeight(true);
        scroller.SetMaxContentHeight(maxHeight);
        scroller.SetChild(box);
        return scroller;
    }

    public static Gtk.Widget Create(MenuItem item, Action? beforeInvoke, int width = 200)
    {
        if (item.HasSubMenu)
        {
            return CreateSubMenu(item, beforeInvoke, width);
        }

        var content = Gtk.Box.New(Gtk.Orientation.Horizontal, 24);

        var text = item.Checked ? $"\u2713  {item.DisplayText}" : item.DisplayText;
        var label = Gtk.Label.New(text);
        label.SetHalign(Gtk.Align.Start);
        label.SetHexpand(true);
        content.Append(label);

        if (item.ShortcutDisplayText is { } hint)
        {
            var shortcut = Gtk.Label.New(hint);
            shortcut.SetHalign(Gtk.Align.End);
            shortcut.AddCssClass("dim-label");
            content.Append(shortcut);
        }

        var button = Gtk.Button.New();
        button.SetChild(content);
        button.SetHalign(Gtk.Align.Fill);
        button.AddCssClass("flat");
        button.SetSensitive(item.Enabled);
        button.OnClicked += (_, _) =>
        {
            // Close before running the command so the command is free to open another menu.
            beforeInvoke?.Invoke();
            item.Invoke();
        };

        return button;
    }

    /// <summary>
    /// A row that opens its submenu in a popover of its own. <c>Gtk.MenuButton</c> is what keeps the
    /// menu that holds it open while the submenu is up, so the pair behaves like a native menu.
    /// </summary>
    private static Gtk.Widget CreateSubMenu(MenuItem item, Action? beforeInvoke, int width)
    {
        var content = Gtk.Box.New(Gtk.Orientation.Horizontal, 12);

        var label = Gtk.Label.New(item.DisplayText);
        label.SetHalign(Gtk.Align.Start);
        label.SetHexpand(true);
        content.Append(label);
        content.Append(Gtk.Image.NewFromIconName("go-next-symbolic"));

        var popover = Gtk.Popover.New();
        popover.SetChild(CreateList(item.TrimmedSubItems, beforeInvoke, width, MaxMenuHeight));
        popover.SetHasArrow(false);
        popover.SetAutohide(true);

        var button = Gtk.MenuButton.New();
        button.SetChild(content);
        button.SetHalign(Gtk.Align.Fill);
        button.SetSensitive(item.Enabled);
        button.AddCssClass("flat");
        button.SetPopover(popover);
        return button;
    }

    public static Gtk.Widget CreateSeparator()
    {
        var separator = Gtk.Separator.New(Gtk.Orientation.Horizontal);
        separator.SetMarginTop(4);
        separator.SetMarginBottom(4);
        return separator;
    }
}

namespace Kolvir;

/// <summary>
/// The top-level menu of a window: a row of <see cref="Menu"/>s that macOS renders in the
/// one system-wide menu bar and that Windows and Linux render as a bar inside the window.
/// </summary>
/// <remarks>
/// <para>
/// Plain data, like <see cref="ContextMenu"/>: the platform decides how the bar looks and
/// calls <see cref="MenuItem.Invoke"/> when the user picks an entry, so the library carries
/// no menu drawing code for a bar it does not own.
/// </para>
/// <para>
/// A drop-down can hold submenus, nested as deeply as the application likes through
/// <see cref="MenuItem.SubItems"/>. The library's own bar draws them as further menus; macOS
/// hands the whole chain to AppKit, which draws and drives it the way it drives every other
/// submenu on the system.
/// </para>
/// <para>
/// macOS is a platform limitation: the bar there is always the system menu bar, at the top of the
/// screen, and never a bar drawn inside the window - the platform has one menu bar for the whole
/// process and no way to put one in a window. Everything the bar holds works the same way - the
/// drop-downs, their shortcuts, submenus and icons - but the row of titles itself is the system's,
/// so what <see cref="MenuBarView"/> adds to it is not available: no bar inside the window, no
/// <see cref="MenuBarView.TitleHeight"/> or bar font, and no controls hosted in a bar row. The
/// system bar also follows the active window rather than belonging to one window, and macOS answers
/// mnemonics from the first character of a title rather than from the <c>&amp;</c> marker.
/// </para>
/// </remarks>
public sealed class MenuBar
{
    /// <summary>Creates an empty menu bar.</summary>
    public MenuBar()
    {
    }

    /// <summary>Creates a menu bar holding <paramref name="menus"/>, left to right.</summary>
    public MenuBar(params Menu[] menus)
    {
        ArgumentNullException.ThrowIfNull(menus);

        foreach (var menu in menus)
        {
            Menus.Add(menu);
        }
    }

    /// <summary>The top-level menus, left to right.</summary>
    public IList<Menu> Menus { get; } = new List<Menu>();

    /// <summary>
    /// Runs the first item whose <see cref="MenuItem.Shortcut"/> is <paramref name="key"/>
    /// pressed with <paramref name="modifiers"/>, and reports whether an item claimed the key.
    /// </summary>
    /// <remarks>
    /// A matching but disabled item still claims the key, so a shortcut the user can see greyed out
    /// does not fall through to the focused control and do something else instead. Menus are
    /// searched in the order they appear in the bar, and the items of a submenu are searched before
    /// the next item of the drop-down that holds it. macOS does not come through here - AppKit
    /// answers the key equivalent of the item itself - so this is what keeps a shortcut inside a
    /// submenu working on the platforms that draw their own bar.
    /// </remarks>
    public bool TryInvokeShortcut(Key key, Modifiers modifiers)
    {
        foreach (var menu in Menus)
        {
            if (TryInvokeShortcut(menu.Items, key, modifiers))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryInvokeShortcut(IEnumerable<MenuItem> items, Key key, Modifiers modifiers)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                continue;
            }

            if (item.Shortcut is { } shortcut && shortcut.Matches(key, modifiers))
            {
                item.Invoke();
                return true;
            }

            if (item.HasSubMenu && TryInvokeShortcut(item.SubItems, key, modifiers))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// One top-level entry in an <see cref="MenuBar"/> and the items it drops down.
/// </summary>
public sealed class Menu
{
    /// <summary>Creates a top-level menu with <paramref name="text"/> and <paramref name="items"/>.</summary>
    public Menu(string text, params MenuItem[] items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(items);

        Text = text;
        DisplayText = MenuText.Parse(text, out var mnemonic);
        Mnemonic = mnemonic;

        foreach (var item in items)
        {
            Items.Add(item);
        }
    }

    /// <summary>
    /// The label shown in the menu bar. A <c>&amp;</c> marks the next character as the mnemonic and
    /// is not drawn, so <c>"&amp;File"</c> answers to Alt+F on Windows and Linux. macOS works the
    /// mnemonic out from the first character itself, so there it is <see cref="DisplayText"/> that
    /// is shown.
    /// </summary>
    public string Text { get; }

    /// <summary><see cref="Text"/> with the mnemonic marker removed, which is what gets drawn.</summary>
    public string DisplayText { get; }

    /// <summary>The character <see cref="Text"/> marked with <c>&amp;</c>, or <c>'\0'</c> for none.</summary>
    public char Mnemonic { get; }

    /// <summary>The drop-down entries, top to bottom. Use <see cref="MenuItem.Separator"/> for dividers.</summary>
    public IList<MenuItem> Items { get; } = new List<MenuItem>();

    /// <summary><see cref="Items"/> with the separators that would look wrong dropped.</summary>
    public IReadOnlyList<MenuItem> TrimmedItems => MenuTrimming.Trim((IReadOnlyList<MenuItem>)Items);
}

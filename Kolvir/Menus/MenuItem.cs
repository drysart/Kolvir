using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A single entry in an <see cref="ContextMenu"/>. Items are plain data: the presenter
/// decides how they look, and calls <see cref="Invoke"/> when the user picks one.
/// </summary>
/// <remarks>
/// A <c>&amp;</c> in <see cref="Text"/> marks the next character as the item's mnemonic without
/// being drawn, so <c>"&amp;Save"</c> shows "Save" with the S underlined and answers to S while the
/// menu is open. Write <c>&amp;&amp;</c> for a literal ampersand.
/// </remarks>
public sealed class MenuItem
{
    private MenuItem(string text, Action? command, bool isSeparator)
    {
        Text = text;
        Command = command;
        IsSeparator = isSeparator;
        DisplayText = MenuText.Parse(text, out var mnemonic);
        Mnemonic = mnemonic;
    }

    public MenuItem(string text)
        : this(text, null, false)
    {
    }

    public MenuItem(string text, Action? command)
        : this(text, command, false)
    {
    }

    /// <summary>The label to show for the item. Empty for a separator.</summary>
    public string Text { get; }

    /// <summary><see cref="Text"/> with the mnemonic marker removed, which is what gets drawn.</summary>
    public string DisplayText { get; }

    /// <summary>
    /// The character <see cref="Text"/> marked with <c>&amp;</c>, upper-cased, or <c>'\0'</c> when
    /// it marked none.
    /// </summary>
    public char Mnemonic { get; }

    /// <summary>What picking the item does. Null makes the item decorative.</summary>
    public Action? Command { get; }

    /// <summary>True for a separator line rather than a pickable item.</summary>
    public bool IsSeparator { get; }

    /// <summary>
    /// A submenu that opens to the side of this item, or an empty list for an ordinary item.
    /// A parent item is pickable - clicking it opens the submenu - but its own
    /// <see cref="Command"/> is not run.
    /// </summary>
    public IList<MenuItem> SubItems { get; } = new List<MenuItem>();

    /// <summary>True when this item opens a submenu rather than doing something itself.</summary>
    public bool HasSubMenu => SubItems.Count > 0;

    /// <summary>
    /// <see cref="SubItems"/> with the separators a menu would not draw removed, which is what a
    /// presenter that builds a native submenu should show.
    /// </summary>
    public IReadOnlyList<MenuItem> TrimmedSubItems =>
        MenuTrimming.Trim((IReadOnlyList<MenuItem>)SubItems);

    /// <summary>
    /// The icon drawn in the column to the left of the label, or null for none. The item does not
    /// own the image: a menu that loads images disposes them with whatever owns them.
    /// </summary>
    public SKImage? Image { get; set; }

    /// <summary>
    /// A control drawn in the item's row instead of a label, which is how a menu carries a slider,
    /// a text box or a colour swatch. Null for an ordinary item.
    /// </summary>
    /// <remarks>
    /// A control has one parent, so an item that hosts one can only be shown by one menu at a time;
    /// the presenter adds it to its own surface and hands it back when the menu closes. Such a row
    /// is not pickable, because every press in it belongs to the control.
    /// </remarks>
    public Control? Control { get; set; }

    /// <summary>False greys the item out and stops <see cref="Invoke"/> from running its command.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>True to show a check mark next to the item.</summary>
    public bool Checked { get; set; }

    /// <summary>True for the item the platform should activate on a double click or Enter.</summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// The accelerator that runs this item from the keyboard, or null for none. Setting it is what
    /// makes the shortcut work rather than merely appear: the window checks its menu bar before the
    /// focused control sees the key, and the macOS backend turns it into a native key equivalent.
    /// </summary>
    public Shortcut? Shortcut { get; set; }

    /// <summary>
    /// Overrides the hint shown next to the text, for a menu that wants to describe a key it does
    /// not dispatch itself. Null shows <see cref="Shortcut"/> instead.
    /// </summary>
    public string? ShortcutText { get; set; }

    /// <summary>The hint a presenter should draw to the right of the label, or null for none.</summary>
    public string? ShortcutDisplayText => ShortcutText ?? Shortcut?.DisplayText;

    /// <summary>A separator line between two groups of items.</summary>
    public static MenuItem Separator { get; } = new(string.Empty, null, true);

    /// <summary>
    /// Runs the item's command. Returns false when the item is a separator, is disabled, hosts a
    /// control or opens a submenu, so a presenter never has to repeat those checks.
    /// </summary>
    public bool Invoke()
    {
        if (IsSeparator || !Enabled || HasSubMenu || Control is not null)
        {
            return false;
        }

        Command?.Invoke();
        return true;
    }
}

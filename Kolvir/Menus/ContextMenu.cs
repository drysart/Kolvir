using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A context menu: an ordered list of items shown at the pointer when the user asks for one.
/// The menu itself draws nothing - <see cref="ContextMenuPresenter.Current"/> hands it to
/// the platform layer, which is free to show a native menu.
/// </summary>
public sealed class ContextMenu
{
    private readonly List<MenuItem> _items = new();

    /// <summary>Creates an empty menu.</summary>
    public ContextMenu()
    {
    }

    /// <summary>The items in the order they should appear.</summary>
    public IReadOnlyList<MenuItem> Items => _items;

    /// <summary>True when the menu has nothing worth showing: no items, or only separators.</summary>
    public bool IsEmpty
    {
        get
        {
            foreach (var item in _items)
            {
                if (!item.IsSeparator)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// The items with the separators that would look wrong dropped: leading, trailing, and doubled.
    /// Renderers use this so a menu never starts or ends on a separator line, and never shows two
    /// in a row where the middle group was empty.
    /// </summary>
    public IReadOnlyList<MenuItem> TrimmedItems => MenuTrimming.Trim(_items);

    /// <summary>Adds an item that runs <paramref name="command"/> when picked.</summary>
    public ContextMenu Add(string text, Action? command = null) => Add(new MenuItem(text, command));

    /// <summary>
    /// Appends an item the caller has already built, such as one with a submenu or an icon.
    /// </summary>
    /// <param name="item">The item to add to the end of <see cref="Items"/>.</param>
    /// <returns>This menu, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public ContextMenu Add(MenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        return this;
    }

    /// <summary>Adds a separator line between two groups of items.</summary>
    public ContextMenu AddSeparator() => Add(MenuItem.Separator);

    /// <summary>
    /// Inserts an item at <paramref name="index"/>, so a handler can put its own group in front of
    /// what the control contributed, or between two of its groups.
    /// </summary>
    public ContextMenu Insert(int index, MenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Insert(Math.Clamp(index, 0, _items.Count), item);
        return this;
    }

    /// <summary>
    /// Removes every item, including the separators, leaving a menu that <see cref="IsEmpty"/>
    /// reports as empty.
    /// </summary>
    /// <returns>This menu, so calls can be chained.</returns>
    public ContextMenu Clear()
    {
        _items.Clear();
        return this;
    }

    /// <summary>
    /// Builds the menu offered when a selection is right-clicked and nothing else supplies one:
    /// a single Copy item that puts the selection on the clipboard.
    /// </summary>
    public static ContextMenu CreateCopyMenu(UiHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new ContextMenu().Add("Copy", () => host.CopySelection());
    }

    /// <summary>
    /// Asks the platform presenter to show this menu. Returns false when no presenter is
    /// installed, which is the documented no-op fallback: the menu is simply not shown.
    /// </summary>
    public bool Show(UiHost host, Control? control, SKPoint location)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (IsEmpty || ContextMenuPresenter.Current is not { } presenter)
        {
            return false;
        }

        presenter.Show(new ContextMenuRequest(this, host, control, location));
        return true;
    }
}

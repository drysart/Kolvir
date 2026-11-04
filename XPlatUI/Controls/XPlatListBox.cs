using System.Collections;
using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// What an owner-draw handler is given for the item it is asked to paint.
/// </summary>
public class XPlatListBoxDrawItemEventArgs
{
    public XPlatListBoxDrawItemEventArgs(
        SKCanvas canvas,
        int index,
        object? item,
        SKRect bounds,
        XPlatDrawItemState state,
        XPlatFont font,
        SKColor textColor)
    {
        Canvas = canvas;
        Index = index;
        Item = item;
        Bounds = bounds;
        State = state;
        Font = font;
        TextColor = textColor;
    }

    /// <summary>
    /// The canvas to draw on. It is already clipped to the viewport and translated to the list's
    /// own coordinates, so <see cref="Bounds"/> can be used as is.
    /// </summary>
    public SKCanvas Canvas { get; }

    /// <summary>The item's position in <see cref="XPlatListBox.Items"/>.</summary>
    public int Index { get; }

    /// <summary>The item itself.</summary>
    public object? Item { get; }

    /// <summary>The whole row, in the list's coordinates. It may be wider than the viewport.</summary>
    public SKRect Bounds { get; }

    /// <summary>What the row is: selected, focused, hot or disabled.</summary>
    public XPlatDrawItemState State { get; }

    /// <summary>The font the list would have used for the item's text.</summary>
    public XPlatFont Font { get; }

    /// <summary>
    /// The colour the default drawing would have used for the text, which is the right one for a
    /// selected, disabled or plain row. A handler is free to ignore it.
    /// </summary>
    public SKColor TextColor { get; }
}

/// <summary>
/// A vertical list of items, one row each, that the user can select from. It is the closest
/// thing this library has to <c>System.Windows.Forms.ListBox</c>.
/// </summary>
/// <remarks>
/// <para>
/// Items are any object; the row shows <see cref="object.ToString"/>. An
/// <see cref="AddDrawItemHandler"/> replaces that with whatever the application wants to paint.
/// </para>
/// <para>
/// The scrolling, selection, type-ahead and keyboard behaviour all come from
/// <see cref="XPlatVirtualList"/>; this control only supplies the items and the default row
/// drawing.
/// </para>
/// </remarks>
public class XPlatListBox : XPlatVirtualList
{
    private readonly List<object?> _items = [];
    private readonly ItemCollection _itemCollection;

    private Action<object, XPlatListBoxDrawItemEventArgs>? _drawItem;

    public XPlatListBox()
    {
        _itemCollection = new ItemCollection(this, _items);
    }

    /// <summary>The items in the list, in the order they are painted.</summary>
    public ItemCollection Items => _itemCollection;

    /// <summary>Number of items, which is the same as <c>Items.Count</c>.</summary>
    public override int ItemCount => _items.Count;

    /// <summary>
    /// Adds a handler that paints the items instead of the default text. Adding one replaces the
    /// default row drawing entirely, background included.
    /// </summary>
    public IDisposable AddDrawItemHandler(Action<object, XPlatListBoxDrawItemEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _drawItem += handler;
        Invalidate();
        return new XPlatHandlerToken<Action<object, XPlatListBoxDrawItemEventArgs>>(RemoveDrawItemHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddDrawItemHandler"/> added.</summary>
    public void RemoveDrawItemHandler(Action<object, XPlatListBoxDrawItemEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (_drawItem is not null)
        {
            _drawItem -= handler;
        }

        Invalidate();
    }

    /// <summary>The text a row shows, which is the item's own <see cref="object.ToString"/>.</summary>
    public override string GetItemText(int index) => GetItemTextOf(_items[index]);

    /// <summary>The item at <paramref name="index"/>.</summary>
    public override object? GetItem(int index) => _items[index];

    /// <summary>
    /// The text a row shows for an item, which is its own <see cref="object.ToString"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately not an overload of <see cref="GetItemText(int)"/>: with both in scope the
    /// compiler resolves an <c>int</c> argument to this one, because the index overload is an
    /// override, and the rows would show their index instead of their item.
    /// </remarks>
    protected virtual string GetItemTextOf(object? item) => item?.ToString() ?? string.Empty;

    protected override float MeasureItemWidth()
    {
        float width = 0f;
        foreach (var item in _items)
        {
            width = MathF.Max(width, Font.MeasureWidth(GetItemTextOf(item)));
        }

        return width + (TextInset * 2f);
    }

    protected override void OnDrawItem(SKCanvas canvas, int index, SKRect bounds, XPlatDrawItemState state, SKColor textColor)
    {
        if (_drawItem is { } drawItem)
        {
            drawItem(this, new XPlatListBoxDrawItemEventArgs(canvas, index, _items[index], bounds, state, Font, textColor));
            return;
        }

        DrawItemText(canvas, bounds, GetItemText(index), textColor);

        if ((state & XPlatDrawItemState.Focused) != 0)
        {
            DrawFocusRectangle(canvas, bounds, textColor);
        }
    }

    /// <summary>The items of an <see cref="XPlatListBox"/>, in the order they are painted.</summary>
    public sealed class ItemCollection : IList<object?>
    {
        private readonly XPlatListBox _owner;
        private readonly List<object?> _items;

        internal ItemCollection(XPlatListBox owner, List<object?> items)
        {
            _owner = owner;
            _items = items;
        }

        public object? this[int index]
        {
            get => _items[index];
            set
            {
                if (Equals(_items[index], value))
                {
                    return;
                }

                _items[index] = value;
                _owner.NotifyItemsChanged();
            }
        }

        public int Count => _items.Count;

        public bool IsReadOnly => false;

        public void Add(object? item)
        {
            _items.Add(item);
            _owner.NotifyItemsChanged();
        }

        /// <summary>Adds several items with a single layout and repaint.</summary>
        public void AddRange(IEnumerable<object?> items)
        {
            ArgumentNullException.ThrowIfNull(items);

            _owner.BeginUpdate();
            try
            {
                foreach (var item in items)
                {
                    _items.Add(item);
                }
            }
            finally
            {
                _owner.EndUpdate();
            }
        }

        public void Insert(int index, object? item)
        {
            _items.Insert(index, item);
            _owner.NotifyItemsChanged();
        }

        public bool Remove(object? item)
        {
            int index = _items.IndexOf(item);
            if (index < 0)
            {
                return false;
            }

            RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            _items.RemoveAt(index);
            _owner.NotifyItemsChanged();
        }

        public void Clear()
        {
            if (_items.Count == 0)
            {
                return;
            }

            _items.Clear();
            _owner.NotifyItemsChanged();
        }

        public bool Contains(object? item) => _items.Contains(item);

        public int IndexOf(object? item) => _items.IndexOf(item);

        public void CopyTo(object?[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        public IEnumerator<object?> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }
}

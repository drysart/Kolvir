using System.Collections;
using SkiaSharp;

namespace XPlatUI;

/// <summary>Which end of the strip an item packs against when the strip has room to spare.</summary>
public enum XPlatToolStripItemAlignment
{
    /// <summary>Packed from the leading edge (left on a horizontal strip).</summary>
    Near = 0,

    /// <summary>Packed from the trailing edge (right on a horizontal strip).</summary>
    Far = 1,
}

/// <summary>
/// One entry in an <see cref="XPlatToolStrip"/>. Items are plain data plus a small paint routine
/// rather than child controls, which is what makes a tool strip cheap: a strip of ten items is one
/// control and one paint pass instead of eleven controls and eleven.
/// </summary>
public abstract class XPlatToolStripItem
{
    private string _text = string.Empty;
    private XPlatFont? _font;
    private bool _enabled = true;
    private bool _visible = true;
    private bool _spring;
    private float _width = float.NaN;
    private XPlatToolStripItemAlignment _alignment;

    internal XPlatToolStrip? Owner { get; set; }

    /// <summary>The item's caption, for the item kinds that show one.</summary>
    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_text == value)
            {
                return;
            }

            _text = value;
            NotifyChanged();
        }
    }

    /// <summary>The item's font, or <c>null</c> to inherit the strip's.</summary>
    public XPlatFont? Font
    {
        get => _font;
        set
        {
            if (ReferenceEquals(_font, value))
            {
                return;
            }

            _font = value;
            NotifyChanged();
        }
    }

    /// <summary>Whether the item reacts to the mouse. A disabled item is drawn greyed out.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            NotifyChanged();
        }
    }

    /// <summary>Whether the item takes part in layout and painting at all.</summary>
    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value)
            {
                return;
            }

            _visible = value;
            NotifyChanged();
        }
    }

    /// <summary>
    /// Whether the item grows to swallow the strip's spare room. Two springs split it evenly, which
    /// is how a strip pushes its trailing items to the far end.
    /// </summary>
    public bool Spring
    {
        get => _spring;
        set
        {
            if (_spring == value)
            {
                return;
            }

            _spring = value;
            NotifyChanged();
        }
    }

    /// <summary>The item's width, or <see cref="float.NaN"/> to size to its content.</summary>
    public float Width
    {
        get => _width;
        set
        {
            if (_width.Equals(value))
            {
                return;
            }

            _width = value;
            NotifyChanged();
        }
    }

    /// <summary>Which end of the strip the item packs against.</summary>
    public XPlatToolStripItemAlignment Alignment
    {
        get => _alignment;
        set
        {
            if (_alignment == value)
            {
                return;
            }

            _alignment = value;
            NotifyChanged();
        }
    }

    /// <summary>Whether the item can be clicked or focused. Only buttons qualify.</summary>
    public virtual bool IsInteractive => false;

    /// <summary>The font this item draws with, after inheritance from the strip.</summary>
    protected XPlatFont ResolveFont() => _font ?? Owner?.Font ?? XPlatTheme.DefaultFont;

    /// <summary>Width the item wants, given the height it will be given.</summary>
    internal virtual float MeasureWidth(float height) => 0f;

    /// <summary>Paints the item inside the rectangle the strip assigned it.</summary>
    internal virtual void Paint(SKCanvas canvas, SKRect rect, bool enabled, bool hot, bool pressed)
    {
    }

    /// <summary>Runs the item's command, as if it had been clicked.</summary>
    internal virtual void Activate()
    {
    }

    private protected void NotifyChanged() => Owner?.OnItemChanged(this);
}

/// <summary>A static caption on a tool strip.</summary>
public class XPlatToolStripLabel : XPlatToolStripItem
{
    /// <summary>Space either side of the caption.</summary>
    public const float HorizontalPadding = 6f;

    private XPlatContentAlignment _textAlign = XPlatContentAlignment.MiddleLeft;
    private XPlatThemeColor _foreColor = XPlatThemeColor.Foreground;

    /// <summary>Creates a label with no caption.</summary>
    public XPlatToolStripLabel()
    {
    }

    /// <summary>Creates a label with a caption.</summary>
    public XPlatToolStripLabel(string text) => Text = text;

    /// <summary>The caption's colour role, resolved at paint time.</summary>
    public XPlatThemeColor ForeColor
    {
        get => _foreColor;
        set
        {
            if (_foreColor == value)
            {
                return;
            }

            _foreColor = value;
            NotifyChanged();
        }
    }

    /// <summary>Where the caption sits inside its rectangle.</summary>
    public XPlatContentAlignment TextAlign
    {
        get => _textAlign;
        set
        {
            if (_textAlign == value)
            {
                return;
            }

            _textAlign = value;
            NotifyChanged();
        }
    }

    internal override float MeasureWidth(float height)
    {
        float textWidth = XPlatTextRenderer.Measure(Text, ResolveFont(), float.PositiveInfinity, wrap: false).Width;
        return textWidth + (HorizontalPadding * 2f);
    }

    internal override void Paint(SKCanvas canvas, SKRect rect, bool enabled, bool hot, bool pressed)
    {
        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        var color = enabled ? XPlatTheme.ColorOf(_foreColor) : XPlatTheme.DisabledForeground;
        var inner = XPlatGeometry.Deflate(rect, new XPlatThickness(HorizontalPadding, 0f));
        XPlatTextRenderer.Draw(canvas, Text, ResolveFont(), color, inner, _textAlign, wrap: false);
    }
}

/// <summary>A clickable button on a tool strip.</summary>
public class XPlatToolStripButton : XPlatToolStripItem
{
    /// <summary>Space either side of the caption, which doubles as the hover pill's margin.</summary>
    public const float HorizontalPadding = 10f;

    /// <summary>Corner radius of the hover and press fill.</summary>
    public const float CornerRadius = 5f;

    private Action<object>? _click;

    /// <summary>Creates a button with no caption.</summary>
    public XPlatToolStripButton()
    {
    }

    /// <summary>Creates a button with a caption.</summary>
    public XPlatToolStripButton(string text) => Text = text;

    /// <inheritdoc />
    public override bool IsInteractive => true;

    /// <summary>Runs the handler when the button is clicked or activated from the keyboard.</summary>
    public XPlatHandlerToken<Action<object>> AddClickHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _click += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveClickHandler, handler);
    }

    /// <summary>Removes a click handler.</summary>
    public void RemoveClickHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _click -= handler;
    }

    internal override float MeasureWidth(float height)
    {
        float textWidth = XPlatTextRenderer.Measure(Text, ResolveFont(), float.PositiveInfinity, wrap: false).Width;
        return textWidth + (HorizontalPadding * 2f);
    }

    internal override void Paint(SKCanvas canvas, SKRect rect, bool enabled, bool hot, bool pressed)
    {
        if (pressed)
        {
            XPlatPainter.Fill(canvas, rect, XPlatTheme.SecondaryPressed, CornerRadius);
        }
        else if (hot && enabled)
        {
            XPlatPainter.Fill(canvas, rect, XPlatTheme.SecondaryHover, CornerRadius);
        }

        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        var color = enabled ? XPlatTheme.Foreground : XPlatTheme.DisabledForeground;
        var inner = XPlatGeometry.Deflate(rect, new XPlatThickness(HorizontalPadding, 0f));
        XPlatTextRenderer.Draw(canvas, Text, ResolveFont(), color, inner, XPlatContentAlignment.MiddleCenter, wrap: false);
    }

    internal override void Activate() => _click?.Invoke(this);
}

/// <summary>A vertical hairline that groups the items either side of it.</summary>
public class XPlatToolStripSeparator : XPlatToolStripItem
{
    /// <summary>Space either side of the line.</summary>
    public const float Margin = 5f;

    internal override float MeasureWidth(float height) => 1f + (Margin * 2f);

    internal override void Paint(SKCanvas canvas, SKRect rect, bool enabled, bool hot, bool pressed)
    {
        float x = rect.MidX;
        XPlatPainter.Fill(
            canvas,
            new SKRect(x, rect.Top + 3f, x + 1f, rect.Bottom - 3f),
            enabled ? XPlatTheme.Border : XPlatTheme.DisabledBackground);
    }
}

/// <summary>A progress read-out embedded in a status strip.</summary>
public class XPlatToolStripProgressBar : XPlatToolStripItem
{
    /// <summary>Width used when <see cref="XPlatToolStripItem.Width"/> is left unset.</summary>
    public const float DefaultWidth = 120f;

    /// <summary>Height of the bar itself, centred in the item's rectangle.</summary>
    public const float BarHeight = 8f;

    /// <summary>Space either side of the bar.</summary>
    public const float HorizontalPadding = 4f;

    private float _minimum;
    private float _maximum = 100f;
    private float _value;

    /// <summary>The value that fills the bar completely.</summary>
    public float Maximum
    {
        get => _maximum;
        set
        {
            value = MathF.Max(value, _minimum);
            if (_maximum.Equals(value))
            {
                return;
            }

            _maximum = value;
            if (_value > _maximum)
            {
                _value = _maximum;
            }

            NotifyChanged();
        }
    }

    /// <summary>The value that empties the bar.</summary>
    public float Minimum
    {
        get => _minimum;
        set
        {
            value = MathF.Min(value, _maximum);
            if (_minimum.Equals(value))
            {
                return;
            }

            _minimum = value;
            if (_value < _minimum)
            {
                _value = _minimum;
            }

            NotifyChanged();
        }
    }

    /// <summary>The current value, clamped to <see cref="Minimum"/>..<see cref="Maximum"/>.</summary>
    public float Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, _minimum, _maximum);
            if (_value.Equals(value))
            {
                return;
            }

            _value = value;
            NotifyChanged();
        }
    }

    /// <summary>How full the bar is, from 0 to 1.</summary>
    public float Percent
    {
        get
        {
            float range = _maximum - _minimum;
            return range <= 0f ? 0f : Math.Clamp((_value - _minimum) / range, 0f, 1f);
        }
    }

    internal override float MeasureWidth(float height) => float.IsNaN(Width) ? DefaultWidth : Width;

    internal override void Paint(SKCanvas canvas, SKRect rect, bool enabled, bool hot, bool pressed)
    {
        var inner = XPlatGeometry.Deflate(rect, new XPlatThickness(HorizontalPadding, 0f));
        float barHeight = MathF.Min(BarHeight, inner.Height);
        if (barHeight <= 0f || inner.Width <= 0f)
        {
            return;
        }

        float top = inner.MidY - (barHeight / 2f);
        var track = new SKRect(inner.Left, top, inner.Right, top + barHeight);
        float radius = barHeight / 2f;
        XPlatPainter.Fill(canvas, track, enabled ? XPlatTheme.Secondary : XPlatTheme.DisabledBackground, radius);

        float percent = Percent;
        if (percent <= 0f)
        {
            return;
        }

        float filled = MathF.Max(track.Width * percent, barHeight);
        var fill = new SKRect(track.Left, track.Top, track.Left + filled, track.Bottom);
        XPlatPainter.Fill(canvas, fill, enabled ? XPlatTheme.Accent : XPlatTheme.DisabledForeground, radius);
    }
}

/// <summary>
/// Elastic space. Its width is whatever the strip has left over, which is how a status strip pushes
/// a progress bar to the far end.
/// </summary>
public class XPlatToolStripSpring : XPlatToolStripItem
{
    /// <summary>Creates a spring.</summary>
    public XPlatToolStripSpring() => Spring = true;
}

/// <summary>
/// A docked bar of small items, like WinForms' <c>StatusStrip</c> and <c>ToolStrip</c> rolled into
/// one. It holds <see cref="XPlatToolStripItem"/>s rather than child controls; see
/// <see cref="XPlatStatusStrip"/> for the status-bar defaults.
/// </summary>
public class XPlatToolStrip : XPlatControl
{
    private const float ItemSpacing = 4f;
    private const float NaturalHeight = 24f;

    private readonly List<XPlatToolStripItem> _items = new();
    private readonly List<Entry> _layout = new();
    private XPlatToolStripItemCollection? _itemCollection;
    private XPlatToolStripItem? _hot;
    private XPlatToolStripItem? _pressed;
    private XPlatToolStripItem? _selected;
    private bool _keyPressed;
    private bool _layoutDirty = true;
    private SKSize _layoutSize;

    /// <summary>Creates a horizontal strip docked to the top of its container.</summary>
    public XPlatToolStrip()
    {
        Dock = DockStyle.Top;
        TabStop = true;
        Height = 30f;
        Padding = new XPlatThickness(8f, 3f);
        MinimumSize = new SKSize(0f, 26f);
    }

    /// <summary>The items in the strip, in paint order.</summary>
    public IList<XPlatToolStripItem> Items => _itemCollection ??= new XPlatToolStripItemCollection(this, _items);

    /// <summary>The items inherit this strip's effective font when they do not set their own.</summary>
    protected override void OnAppearanceChanged() => _layoutDirty = true;

    /// <inheritdoc />
    protected override XPlatBackground DefaultBackground => XPlatBackground.FromThemeColor(XPlatThemeColor.Surface);

    /// <inheritdoc />
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        float width = 0f;
        int count = 0;
        foreach (var item in _items)
        {
            if (!item.Visible)
            {
                continue;
            }

            width += (float.IsNaN(item.Width) ? item.MeasureWidth(NaturalHeight) : item.Width) + ItemSpacing;
            count++;
        }

        if (count > 0)
        {
            width -= ItemSpacing;
        }

        return new SKSize(width, NaturalHeight);
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        base.OnPaintBackground(canvas, rect);

        var edge = EdgeLine(rect);
        if (edge.Width > 0f || edge.Height > 0f)
        {
            XPlatPainter.Fill(canvas, edge, XPlatTheme.Border);
        }
    }

    /// <inheritdoc />
    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        EnsureLayout();

        foreach (var entry in _layout)
        {
            var itemRect = entry.Rect;
            if (!itemRect.IntersectsWith(ContentRect))
            {
                continue;
            }

            bool enabled = Enabled && entry.Item.Enabled;
            bool hot = ReferenceEquals(entry.Item, _hot);
            bool pressed = ReferenceEquals(entry.Item, _pressed) || (_keyPressed && ReferenceEquals(entry.Item, _selected));
            entry.Item.Paint(canvas, itemRect, enabled, hot, pressed);

            if (IsFocused && ReferenceEquals(entry.Item, _selected))
            {
                XPlatPainter.DrawFocusRing(canvas, itemRect, XPlatTheme.FocusRing, 2f, XPlatToolStripButton.CornerRadius);
            }
        }
    }

    /// <inheritdoc />
    protected override void OnBoundsChanged()
    {
        _layoutDirty = true;
        base.OnBoundsChanged();
    }

    /// <inheritdoc />
    protected override void OnMouseDown(XPlatMouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != XPlatMouseButtons.Left)
        {
            return;
        }

        var hit = ItemAt(e.Location);
        if (hit is null)
        {
            return;
        }

        _pressed = hit;
        _hot = hit;
        _selected = hit;
        CaptureMouse();
        Invalidate();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnMouseMove(XPlatMouseEventArgs e)
    {
        base.OnMouseMove(e);

        var hit = ItemAt(e.Location);
        if (ReferenceEquals(hit, _hot))
        {
            return;
        }

        _hot = hit;
        Cursor = hit is null ? XPlatCursor.Default : XPlatCursor.Hand;
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnMouseUp(XPlatMouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != XPlatMouseButtons.Left || _pressed is null)
        {
            return;
        }

        var pressed = _pressed;
        _pressed = null;
        ReleaseMouseCapture();
        Invalidate();

        if (ReferenceEquals(ItemAt(e.Location), pressed))
        {
            pressed.Activate();
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();

        bool changed = _hot is not null || _pressed is not null;
        _hot = null;
        if (_pressed is not null)
        {
            _pressed = null;
            ReleaseMouseCapture();
        }

        Cursor = XPlatCursor.Default;
        if (changed)
        {
            Invalidate();
        }
    }

    /// <inheritdoc />
    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        _selected = FirstFocusable();
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        _selected = null;
        _keyPressed = false;
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnEnabledChanged()
    {
        base.OnEnabledChanged();
        _hot = null;
        _pressed = null;
        _keyPressed = false;
        if (!Enabled)
        {
            _selected = null;
        }

        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(XPlatKeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled)
        {
            return;
        }

        switch (e.Key)
        {
            case XPlatKey.Left:
            case XPlatKey.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case XPlatKey.Right:
            case XPlatKey.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case XPlatKey.Home:
                Select(FirstFocusable());
                e.Handled = true;
                break;
            case XPlatKey.End:
                Select(LastFocusable());
                e.Handled = true;
                break;
            case XPlatKey.Enter:
            case XPlatKey.Space:
                if (_selected is not null)
                {
                    _keyPressed = true;
                    Invalidate();
                    e.Handled = true;
                }

                break;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyUp(XPlatKeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (!_keyPressed || (e.Key != XPlatKey.Enter && e.Key != XPlatKey.Space))
        {
            return;
        }

        _keyPressed = false;
        var selected = _selected;
        Invalidate();
        if (selected is not null && Enabled && selected.Enabled)
        {
            selected.Activate();
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _itemCollection?.Detach();
            _itemCollection = null;
        }

        base.Dispose(disposing);
    }

    internal void OnItemChanged(XPlatToolStripItem item)
    {
        _layoutDirty = true;
        if (ReferenceEquals(item, _hot) && !item.Enabled)
        {
            _hot = null;
        }

        if (ReferenceEquals(item, _selected) && !IsFocusable(item))
        {
            _selected = null;
        }

        UpdateTabStop();
        Invalidate();
    }

    internal void OnItemsChanged()
    {
        _layoutDirty = true;
        if (_selected is not null && !IsFocusable(_selected))
        {
            _selected = null;
        }

        UpdateTabStop();
        Invalidate();
    }

    /// <summary>The hairline drawn along the edge that faces the client area.</summary>
    private SKRect EdgeLine(SKRect rect) => Dock switch
    {
        DockStyle.Top => new SKRect(rect.Left, rect.Bottom - 1f, rect.Right, rect.Bottom),
        DockStyle.Bottom => new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + 1f),
        DockStyle.Left => new SKRect(rect.Right - 1f, rect.Top, rect.Right, rect.Bottom),
        DockStyle.Right => new SKRect(rect.Left, rect.Top, rect.Left + 1f, rect.Bottom),
        _ => new SKRect(rect.Left, rect.Bottom - 1f, rect.Right, rect.Bottom),
    };

    private void EnsureLayout()
    {
        var content = ContentRect;
        if (!_layoutDirty && _layoutSize.Width == content.Width && _layoutSize.Height == content.Height)
        {
            return;
        }

        _layoutDirty = false;
        _layoutSize = new SKSize(content.Width, content.Height);
        _layout.Clear();

        float height = content.Height;
        if (height <= 0f || content.Width <= 0f)
        {
            return;
        }

        float nearWidth = 0f;
        float farWidth = 0f;
        int springs = 0;
        foreach (var item in _items)
        {
            if (!item.Visible)
            {
                continue;
            }

            float width = MathF.Max(0f, float.IsNaN(item.Width) ? item.MeasureWidth(height) : item.Width);
            _layout.Add(new Entry { Item = item, Width = width, Alignment = item.Alignment });
            if (item.Alignment == XPlatToolStripItemAlignment.Far)
            {
                farWidth += width + ItemSpacing;
            }
            else
            {
                nearWidth += width + ItemSpacing;
            }

            if (item.Spring)
            {
                springs++;
            }
        }

        if (_layout.Count == 0)
        {
            return;
        }

        float slack = content.Width - nearWidth - farWidth;
        if (springs > 0 && slack > 0f)
        {
            float share = slack / springs;
            for (int i = 0; i < _layout.Count; i++)
            {
                if (_layout[i].Item.Spring)
                {
                    var spring = _layout[i];
                    spring.Width += share;
                    _layout[i] = spring;
                }
            }
        }

        float x = content.Left;
        for (int i = 0; i < _layout.Count; i++)
        {
            var entry = _layout[i];
            if (entry.Alignment != XPlatToolStripItemAlignment.Near)
            {
                continue;
            }

            entry.Rect = new SKRect(x, content.Top, x + entry.Width, content.Bottom);
            _layout[i] = entry;
            x += entry.Width + ItemSpacing;
        }

        float right = content.Right;
        for (int i = _layout.Count - 1; i >= 0; i--)
        {
            var entry = _layout[i];
            if (entry.Alignment != XPlatToolStripItemAlignment.Far)
            {
                continue;
            }

            right -= entry.Width;
            entry.Rect = new SKRect(right, content.Top, right + entry.Width, content.Bottom);
            _layout[i] = entry;
            right -= ItemSpacing;
        }
    }

    private XPlatToolStripItem? ItemAt(SKPoint point)
    {
        if (!XPlatGeometry.Contains(ContentRect, point))
        {
            return null;
        }

        for (int i = _layout.Count - 1; i >= 0; i--)
        {
            var entry = _layout[i];
            if (!entry.Item.IsInteractive || !XPlatGeometry.Contains(entry.Rect, point))
            {
                continue;
            }

            return IsFocusable(entry.Item) ? entry.Item : null;
        }

        return null;
    }

    private bool IsFocusable(XPlatToolStripItem item) => item.Visible && item.Enabled && item.IsInteractive;

    private XPlatToolStripItem? FirstFocusable()
    {
        foreach (var item in _items)
        {
            if (IsFocusable(item))
            {
                return item;
            }
        }

        return null;
    }

    private XPlatToolStripItem? LastFocusable()
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (IsFocusable(_items[i]))
            {
                return _items[i];
            }
        }

        return null;
    }

    private void MoveSelection(int direction)
    {
        var items = new List<XPlatToolStripItem>();
        foreach (var item in _items)
        {
            if (IsFocusable(item))
            {
                items.Add(item);
            }
        }

        if (items.Count == 0)
        {
            return;
        }

        int index = _selected is null ? -1 : items.IndexOf(_selected);
        int next = index < 0
            ? (direction > 0 ? 0 : items.Count - 1)
            : ((index + direction) % items.Count + items.Count) % items.Count;
        Select(items[next]);
    }

    private void Select(XPlatToolStripItem? item)
    {
        if (ReferenceEquals(item, _selected))
        {
            return;
        }

        _selected = item;
        Invalidate();
    }

    private void UpdateTabStop()
    {
        bool focusable = false;
        foreach (var item in _items)
        {
            if (IsFocusable(item))
            {
                focusable = true;
                break;
            }
        }

        TabStop = focusable;
    }

    /// <summary>One placed item: what it is, how wide it ended up, and where it sits.</summary>
    private struct Entry
    {
        public XPlatToolStripItem Item;
        public float Width;
        public XPlatToolStripItemAlignment Alignment;
        public SKRect Rect;
    }
}

/// <summary>
/// A tool strip with the status-bar defaults: docked to the bottom, a little shorter, and nothing
/// focusable unless it holds buttons.
/// </summary>
public class XPlatStatusStrip : XPlatToolStrip
{
    /// <summary>Creates a horizontal status strip docked to the bottom of its container.</summary>
    public XPlatStatusStrip()
    {
        Dock = DockStyle.Bottom;
        Height = 26f;
        Padding = new XPlatThickness(8f, 2f);
        MinimumSize = new SKSize(0f, 22f);
    }
}

/// <summary>
/// The strip's item list. Adding or removing an item tells the strip to re-measure and repaint, and
/// keeps the back-pointer that lets an item notify its owner when one of its own properties change.
/// </summary>
public sealed class XPlatToolStripItemCollection : IList<XPlatToolStripItem>
{
    private readonly XPlatToolStrip _owner;
    private readonly List<XPlatToolStripItem> _items;

    internal XPlatToolStripItemCollection(XPlatToolStrip owner, List<XPlatToolStripItem> items)
    {
        _owner = owner;
        _items = items;
    }

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public XPlatToolStripItem this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var previous = _items[index];
            if (ReferenceEquals(previous, value))
            {
                return;
            }

            previous.Owner = null;
            _items[index] = value;
            value.Owner = _owner;
            _owner.OnItemsChanged();
        }
    }

    /// <inheritdoc />
    public void Add(XPlatToolStripItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Owner = _owner;
        _items.Add(item);
        _owner.OnItemsChanged();
    }

    /// <summary>Adds several items with a single re-measure.</summary>
    public void AddRange(params XPlatToolStripItem[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            item.Owner = _owner;
            _items.Add(item);
        }

        _owner.OnItemsChanged();
    }

    /// <inheritdoc />
    public void Clear()
    {
        foreach (var item in _items)
        {
            item.Owner = null;
        }

        _items.Clear();
        _owner.OnItemsChanged();
    }

    /// <inheritdoc />
    public bool Contains(XPlatToolStripItem item) => _items.Contains(item);

    /// <inheritdoc />
    public void CopyTo(XPlatToolStripItem[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public IEnumerator<XPlatToolStripItem> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    public int IndexOf(XPlatToolStripItem item) => _items.IndexOf(item);

    /// <inheritdoc />
    public void Insert(int index, XPlatToolStripItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Owner = _owner;
        _items.Insert(index, item);
        _owner.OnItemsChanged();
    }

    /// <inheritdoc />
    public bool Remove(XPlatToolStripItem item)
    {
        if (!_items.Remove(item))
        {
            return false;
        }

        item.Owner = null;
        _owner.OnItemsChanged();
        return true;
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        item.Owner = null;
        _owner.OnItemsChanged();
    }

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    internal void Detach()
    {
        foreach (var item in _items)
        {
            item.Owner = null;
        }
    }
}

using SkiaSharp;

namespace Kolvir;

/// <summary>
/// Vertical placement of an inline control relative to the line of text it appears on,
/// mirroring CSS <c>vertical-align: top | middle | bottom</c> for inline-blocks.
/// </summary>
public enum InlineAlignment
{
    /// <summary>The control's top edge lines up with the top of the line's text.</summary>
    Top = 0,

    /// <summary>The control is vertically centred on the line's text.</summary>
    Center = 1,

    /// <summary>The control's bottom edge lines up with the bottom of the line's text.</summary>
    Bottom = 2,
}

/// <summary>Event data for the mouse events an <see cref="TextSpan"/> raises.</summary>
public class SpanEventArgs : MouseEventArgs
{
    public SpanEventArgs(
        TextSpan span,
        SKPoint location,
        MouseButtons button = MouseButtons.None,
        Modifiers modifiers = Modifiers.None,
        int clickCount = 1)
        : base(location, button, modifiers, clickCount)
    {
        Span = span;
    }

    /// <summary>The span that raised the event.</summary>
    public TextSpan Span { get; }
}

/// <summary>
/// One run of content inside an <see cref="RichLabel"/>: either formatted text, or -
/// when <see cref="Control"/> is set - an inline control that takes part in the text flow.
/// </summary>
/// <remarks>
/// A span can raise mouse events, which is what makes links possible: give it a
/// <see cref="Cursor"/> and handle <see cref="AddClickHandler"/>. Spans are not controls; they are
/// lightweight value holders owned by the rich label they are added to.
/// </remarks>
public sealed class TextSpan
{
    private Action<object, SpanEventArgs>? _click;
    private Action<object, SpanEventArgs>? _mouseDown;
    private Action<object, SpanEventArgs>? _mouseUp;
    private Action<object, SpanEventArgs>? _mouseMove;
    private Action<object>? _mouseEnter;
    private Action<object>? _mouseLeave;
    private string _text = string.Empty;
    private bool _bold;
    private bool _italic;
    private bool _underline;
    private bool _strikethrough;
    private SKColor? _foreColor;
    private ThemeColor? _themeColor;
    private SKColor? _hoverBackColor;
    private ThemeColor? _hoverBackColorRole;
    private float? _fontSize;
    private string? _fontFamily;
    private Font? _font;
    private Control? _control;
    private InlineAlignment _inlineAlignment = InlineAlignment.Bottom;
    private Cursor? _cursor;

    public TextSpan()
    {
    }

    public TextSpan(string? text) => _text = text ?? string.Empty;

    /// <summary>Adds a handler that runs on mouse release over the span, after a press that started on it.</summary>
    public IDisposable AddClickHandler(Action<object, SpanEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _click += handler;
        return new HandlerToken<Action<object, SpanEventArgs>>(RemoveClickHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddClickHandler"/> added.</summary>
    public void RemoveClickHandler(Action<object, SpanEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _click -= handler;
    }

    public IDisposable AddMouseDownHandler(Action<object, SpanEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseDown += handler;
        return new HandlerToken<Action<object, SpanEventArgs>>(RemoveMouseDownHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddMouseDownHandler"/> added.</summary>
    public void RemoveMouseDownHandler(Action<object, SpanEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseDown -= handler;
    }

    public IDisposable AddMouseUpHandler(Action<object, SpanEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseUp += handler;
        return new HandlerToken<Action<object, SpanEventArgs>>(RemoveMouseUpHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddMouseUpHandler"/> added.</summary>
    public void RemoveMouseUpHandler(Action<object, SpanEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseUp -= handler;
    }

    public IDisposable AddMouseMoveHandler(Action<object, SpanEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseMove += handler;
        return new HandlerToken<Action<object, SpanEventArgs>>(RemoveMouseMoveHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddMouseMoveHandler"/> added.</summary>
    public void RemoveMouseMoveHandler(Action<object, SpanEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseMove -= handler;
    }

    public IDisposable AddMouseEnterHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseEnter += handler;
        return new HandlerToken<Action<object>>(RemoveMouseEnterHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddMouseEnterHandler"/> added.</summary>
    public void RemoveMouseEnterHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseEnter -= handler;
    }

    public IDisposable AddMouseLeaveHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseLeave += handler;
        return new HandlerToken<Action<object>>(RemoveMouseLeaveHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddMouseLeaveHandler"/> added.</summary>
    public void RemoveMouseLeaveHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _mouseLeave -= handler;
    }

    /// <summary>Creates a span that places <paramref name="control"/> in the text flow.</summary>
    public static TextSpan ForControl(Control control, InlineAlignment alignment = InlineAlignment.Bottom)
    {
        ArgumentNullException.ThrowIfNull(control);
        return new TextSpan { Control = control, InlineAlignment = alignment };
    }

    /// <summary>
    /// The text of the span. Line breaks (<c>\n</c>) start a new line; runs of whitespace
    /// collapse to a single break opportunity (<c>\u00A0</c> is not a break opportunity).
    /// Ignored when <see cref="Control"/> is set.
    /// </summary>
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

    public bool Bold
    {
        get => _bold;
        set => Set(ref _bold, value);
    }

    public bool Italic
    {
        get => _italic;
        set => Set(ref _italic, value);
    }

    public bool Underline
    {
        get => _underline;
        set => Set(ref _underline, value);
    }

    public bool Strikethrough
    {
        get => _strikethrough;
        set => Set(ref _strikethrough, value);
    }

    /// <summary>Overrides the label's <see cref="Control.ForeColor"/> for this span.</summary>
    public SKColor? ForeColor
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

    /// <summary>
    /// A palette entry to paint the span in, resolved while painting so the span follows a theme
    /// change. Used when <see cref="ForeColor"/> is not set; <see cref="RichLabel.AddLink"/>
    /// uses <see cref="ThemeColor.Accent"/>.
    /// </summary>
    public ThemeColor? ThemeColor
    {
        get => _themeColor;
        set
        {
            if (_themeColor == value)
            {
                return;
            }

            _themeColor = value;
            NotifyChanged();
        }
    }

    /// <summary>Optional fill painted behind the span while the pointer is over it (handy for links).</summary>
    public SKColor? HoverBackColor
    {
        get => _hoverBackColor;
        set
        {
            if (_hoverBackColor == value)
            {
                return;
            }

            _hoverBackColor = value;
            NotifyChanged();
        }
    }

    /// <summary>
    /// A palette entry to tint the hover fill with, resolved while painting. Used when
    /// <see cref="HoverBackColor"/> is not set; <see cref="RichLabel.AddLink"/> uses
    /// <see cref="ThemeColor.Accent"/>, so a link keeps its highlight across a theme change.
    /// </summary>
    public ThemeColor? HoverBackColorRole
    {
        get => _hoverBackColorRole;
        set
        {
            if (_hoverBackColorRole == value)
            {
                return;
            }

            _hoverBackColorRole = value;
            NotifyChanged();
        }
    }

    /// <summary>Overrides the label's base font size for this span.</summary>
    public float? FontSize
    {
        get => _fontSize;
        set
        {
            if (_fontSize == value)
            {
                return;
            }

            _fontSize = value;
            NotifyChanged();
        }
    }

    /// <summary>Overrides the label's base font family for this span.</summary>
    public string? FontFamily
    {
        get => _fontFamily;
        set
        {
            if (_fontFamily == value)
            {
                return;
            }

            _fontFamily = value;
            NotifyChanged();
        }
    }

    /// <summary>
    /// A fully specified font for this span. When set, <see cref="Bold"/>, <see cref="Italic"/>,
    /// <see cref="FontSize"/> and <see cref="FontFamily"/> are ignored. The span does not own the font.
    /// </summary>
    public Font? Font
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

    /// <summary>
    /// A control rendered in the text flow instead of text. The rich label adds it to its own
    /// child collection and positions it as part of the layout, so it paints, hit-tests and
    /// receives input like any other control.
    /// </summary>
    public Control? Control
    {
        get => _control;
        set
        {
            if (ReferenceEquals(_control, value))
            {
                return;
            }

            _control = value;
            NotifyChanged();
        }
    }

    /// <summary>How the inline <see cref="Control"/> lines up with the text of its line.</summary>
    public InlineAlignment InlineAlignment
    {
        get => _inlineAlignment;
        set
        {
            if (_inlineAlignment == value)
            {
                return;
            }

            _inlineAlignment = value;
            NotifyChanged();
        }
    }

    /// <summary>
    /// Cursor the host shows while the pointer is over the span. Unless set explicitly it is
    /// <see cref="Cursor.Hand"/> for interactive spans and <see cref="Cursor.Default"/>
    /// (i.e. inherit the label's cursor) otherwise.
    /// </summary>
    public Cursor Cursor
    {
        get => _cursor ?? (IsInteractive ? Cursor.Hand : Cursor.Default);
        set => _cursor = value;
    }

    /// <summary>True when the span handles mouse input and should therefore advertise itself as clickable.</summary>
    public bool IsInteractive => _click is not null || _mouseDown is not null || _mouseUp is not null;

    /// <summary>Arbitrary user data associated with the span.</summary>
    public object? Tag { get; set; }

    /// <summary>True while the pointer is over the span.</summary>
    public bool IsMouseOver { get; internal set; }

    /// <summary>True while a mouse button is held down on the span.</summary>
    public bool IsPressed { get; internal set; }

    /// <summary>The label this span belongs to, if any.</summary>
    public RichLabel? Owner { get; internal set; }

    internal void RaiseClick(SpanEventArgs e) => _click?.Invoke(this, e);

    internal void RaiseMouseDown(SpanEventArgs e) => _mouseDown?.Invoke(this, e);

    internal void RaiseMouseUp(SpanEventArgs e) => _mouseUp?.Invoke(this, e);

    internal void RaiseMouseMove(SpanEventArgs e) => _mouseMove?.Invoke(this, e);

    internal void RaiseMouseEnter() => _mouseEnter?.Invoke(this);

    internal void RaiseMouseLeave() => _mouseLeave?.Invoke(this);

    private void Set(ref bool field, bool value)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        NotifyChanged();
    }

    private void NotifyChanged() => Owner?.OnSpanChanged(this);
}

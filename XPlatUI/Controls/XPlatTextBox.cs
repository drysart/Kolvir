using System.Text;
using SkiaSharp;
using XPlatUI.Multiline;
using XPlatUI.Spans;

namespace XPlatUI;

/// <summary>
/// A single-line editable text field with a blinking caret, selection, clipboard support,
/// password masking and placeholder text. Like every XPlat control it has no platform
/// dependency: it paints into an <see cref="SKCanvas"/> and receives mouse, keyboard and
/// text input from an <see cref="XPlatUiHost"/>.
/// </summary>
/// <remarks>
/// The caret is an <see cref="XPlatCaret"/>, so it looks and blinks like the caret of every other
/// editable control and only runs a timer while this text box has focus. Adapters only need to
/// install a platform clipboard into <see cref="XPlatClipboard.Current"/>.
/// </remarks>
public class XPlatTextBox : XPlatControl
{
    private const float HorizontalCaretPadding = 2f;

    private readonly XPlatCaret _caret;
    private XPlatFont _font = XPlatTheme.DefaultFont;
    private string _placeholderText = string.Empty;
    private int _caretIndex;
    private int _anchor;
    private float _scrollX;
    private bool _selecting;
    private int _maxLength;
    private bool _readOnly;
    private bool _isPassword;
    private char _passwordChar = '\u2022';

    public XPlatTextBox()
    {
        TabStop = true;
        Cursor = XPlatCursor.IBeam;
        _caret = new XPlatCaret(this);
        BorderWidth = 1f;
        CornerRadius = 6f;
        Padding = new XPlatThickness(8f, 6f);
        MinimumSize = new SKSize(0f, 30f);

        // A text box is always part of a selection: its own caret and anchor answer for a drag
        // that starts inside it, and the control-level contract answers for a selection that
        // merely passes through it. The two never fight - claiming a selection is exclusive.
        Selectable = true;
        SelectionStyle = XPlatSelectionStyle.Partial;
    }

    public XPlatTextBox(string text)
        : this()
    {
        Text = text;
    }

    /// <summary>An editable field is painted on the input fill until <see cref="XPlatControl.BackColor"/> is assigned.</summary>
    protected override SKColor? DefaultBackColor => XPlatTheme.InputBackground;

    /// <summary>
    /// The multi-line feature when this box opted into it, otherwise null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole seam between the text box and <c>XPlatUI.Multiline</c>, and the one thing
    /// every text box in every application carries for a feature it may never use: a null reference
    /// and a null check. Everything the feature does - measuring, painting, hit testing, the
    /// scrollbars, the keys that mean something else once text spans lines - is called through this
    /// interface, and the interface's only implementation is created by the extension property that
    /// turns the feature on. An application that never sets <c>Multiline</c> therefore publishes
    /// without the feature's code, which is what FEATURE-COST-POLICY.md §4's first rung asks for and
    /// what a field or a member per multi-line concern could not deliver.
    /// </para>
    /// </remarks>
    internal IXPlatTextBoxMultiline? MultilineFeature { get; set; }

    /// <summary>
    /// The span annotation feature when this box has spans, otherwise null. The second of the text
    /// box's two nullable seams, and the same shape as <see cref="MultilineFeature"/>: drawing a
    /// squiggle is the control's own paint path, so it cannot live outside the control, while every
    /// type that knows how to draw one is named only by the extension property that installs this.
    /// A box that never assigns <c>Spans</c> therefore carries the reference and a null check on the
    /// paint path and publishes without the rest.
    /// </summary>
    internal IXPlatTextBoxSpanFeature? SpanFeature { get; set; }

    /// <summary>True when the box edits several lines. Answers from the seam, so it costs one null check.</summary>
    internal bool IsMultiline => MultilineFeature is not null;

    /// <summary>The client area and padding the multi-line feature lays text out against.</summary>
    internal XPlatTextBoxViewport Viewport => new(ClientRect, ContentPadding);

    /// <summary>
    /// The text as a value. A multi-line box normalises what it is given: CRLF and a lone CR become
    /// the single line feed its line model uses, and a tab becomes spaces, because Tab moves the
    /// focus here and the value has to stay something the box can draw. Every path - typing, paste,
    /// insertion, a binding - therefore produces the same value for the same content, and text that
    /// is copied out and pasted back is unchanged.
    /// </summary>
    public override string Text
    {
        get => base.Text;
        set
        {
            var text = value ?? string.Empty;
            base.Text = MultilineFeature?.SanitizeText(text) ?? text;
        }
    }

    /// <summary>
    /// Sets the selection anchor and the caret independently, which is what Shift-extension needs on
    /// a field whose caret moves by visual line. <see cref="Select"/> always puts the caret at the
    /// end of the range, so it cannot express an extension towards the left.
    /// </summary>
    internal void SetSelection(int anchor, int caretIndex)
    {
        ActivateCaret();

        _anchor = Math.Clamp(anchor, 0, Text.Length);
        _caretIndex = Math.Clamp(caretIndex, 0, Text.Length);

        _caret.ResetBlink();
        EnsureCaretVisible();
        Invalidate();
    }

    /// <summary>
    /// The inset the text, caret and selection are laid out inside. Derived controls that draw
    /// their own chrome in the field, such as <see cref="XPlatNumericUpDown"/>'s spinner, widen
    /// it so the text stops short of what they draw instead of running underneath it.
    /// </summary>
    protected virtual XPlatThickness ContentPadding => Padding;

    /// <summary>The area the text is laid out in: the client rect without <see cref="ContentPadding"/>.</summary>
    private protected SKRect TextRect => XPlatGeometry.Deflate(ClientRect, ContentPadding);

    /// <summary>Font used to draw the text. The text box does not take ownership of it.</summary>
    public XPlatFont Font
    {
        get => _font;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_font, value))
            {
                return;
            }

            _font = value;
            ClampScroll();
            Invalidate();
        }
    }

    /// <summary>The insertion point's caret, which this text box owns and positions.</summary>
    public XPlatCaret Caret => _caret;

    /// <summary>
    /// Where the caret is, in this control's client coordinates. The autocomplete feature hangs its
    /// suggestion list under it instead of repeating the text layout. Empty when the field has no
    /// room to lay the text out.
    /// </summary>
    internal SKRect CaretBounds
    {
        get
        {
            if (MultilineFeature is { } multiline)
            {
                return multiline.GetCaretBounds(this, Viewport);
            }

            var content = TextRect;
            if (content.Width <= 0f || content.Height <= 0f)
            {
                return SKRect.Empty;
            }

            float lineHeight = _font.LineHeight;
            float top = content.Top + MathF.Max(0f, (content.Height - lineHeight) / 2f);
            float x = content.Left - _scrollX + MeasureX(DisplayText, _caretIndex);
            return new SKRect(x, top, x + _caret.Width, top + lineHeight);
        }
    }

    /// <summary>Index of the insertion point (0 .. <see cref="XPlatControl.Text"/>.Length).</summary>
    public int CaretIndex
    {
        get => _caretIndex;
        set => MoveCaretTo(value, extendSelection: false);
    }

    /// <summary>Lower bound of the selection; equals <see cref="CaretIndex"/> when nothing is selected.</summary>
    public int SelectionStart => Math.Min(_caretIndex, _anchor);

    public int SelectionLength => Math.Abs(_caretIndex - _anchor);

    public bool HasSelection => _caretIndex != _anchor;

    public string SelectedText => HasSelection ? Text.Substring(SelectionStart, SelectionLength) : string.Empty;

    /// <summary>Maximum number of characters the user may type. 0 means unlimited.</summary>
    public int MaxLength
    {
        get => _maxLength;
        set => _maxLength = Math.Max(0, value);
    }

    /// <summary>When true the text can be selected and copied but not modified.</summary>
    public bool ReadOnly
    {
        get => _readOnly;
        set
        {
            if (_readOnly == value)
            {
                return;
            }

            _readOnly = value;
            Invalidate();
        }
    }

    /// <summary>When true the text is rendered with <see cref="PasswordChar"/> instead of its real characters.</summary>
    public bool IsPassword
    {
        get => _isPassword;
        set
        {
            if (_isPassword == value)
            {
                return;
            }

            _isPassword = value;
            ClampScroll();
            Invalidate();
        }
    }

    /// <summary>The character used to mask the text when <see cref="IsPassword"/> is true.</summary>
    public char PasswordChar
    {
        get => _passwordChar;
        set
        {
            if (_passwordChar == value)
            {
                return;
            }

            _passwordChar = value;
            ClampScroll();
            Invalidate();
        }
    }

    /// <summary>Muted text shown while <see cref="XPlatControl.Text"/> is empty.</summary>
    public string PlaceholderText
    {
        get => _placeholderText;
        set
        {
            value ??= string.Empty;
            if (_placeholderText == value)
            {
                return;
            }

            _placeholderText = value;
            Invalidate();
        }
    }

    /// <summary>Selects <paramref name="length"/> characters starting at <paramref name="start"/>.</summary>
    public void Select(int start, int length)
    {
        ActivateCaret();

        start = Math.Clamp(start, 0, Text.Length);
        int end = Math.Clamp(start + Math.Max(0, length), start, Text.Length);

        _anchor = start;
        _caretIndex = end;
        _caret.ResetBlink();
        EnsureCaretVisible();
        Invalidate();
    }

    public void SelectAll() => Select(0, Text.Length);

    /// <summary>Collapses the selection, leaving the caret where it is.</summary>
    public void ClearSelection()
    {
        if (!HasSelection)
        {
            return;
        }

        _anchor = _caretIndex;
        Invalidate();
    }

    /// <summary>Copies the selection to the clipboard. Returns false when nothing is selected.</summary>
    public bool Copy()
    {
        if (!HasSelection)
        {
            return false;
        }

        XPlatClipboard.SetText(SelectedText);
        return true;
    }

    /// <summary>Copies the selection to the clipboard and deletes it. Returns false when nothing is selected.</summary>
    public bool Cut()
    {
        if (!HasSelection || ReadOnly)
        {
            return false;
        }

        XPlatClipboard.SetText(SelectedText);
        ReplaceRange(SelectionStart, SelectionLength, string.Empty);
        return true;
    }

    /// <summary>Inserts the clipboard text at the caret, replacing the selection. Returns false when there is no text.</summary>
    public bool Paste()
    {
        if (ReadOnly)
        {
            return false;
        }

        var text = Sanitize(XPlatClipboard.GetText());
        if (text.Length == 0)
        {
            return false;
        }

        InsertText(text);
        return true;
    }

    /// <summary>Inserts text at the caret, replacing the selection.</summary>
    public void InsertText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // A line break survives insertion into a multi-line box, normalised the way every other path
        // normalises one. A single-line box keeps exactly the behaviour it always had.
        if (MultilineFeature is { } multiline)
        {
            text = multiline.SanitizeText(text);
        }

        ReplaceRange(SelectionStart, SelectionLength, text);
    }

    /// <summary>
    /// Copies the box's own selection, or the host's selection when the box is only part of one.
    /// </summary>
    private void CopySelection()
    {
        if (!Copy())
        {
            Host?.CopySelection();
        }
    }

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        if (MultilineFeature is { } multiline)
        {
            return multiline.Measure(this, Viewport, availableSize);
        }

        var sample = Text.Length == 0 ? "W" : Text;
        float width = MathF.Min(MathF.Ceiling(_font.MeasureWidth(sample)) + 4f, 320f);
        return new SKSize(width, MathF.Ceiling(_font.LineHeight) + 2f);
    }

    /// <summary>
    /// Maps a point to the nearest character boundary rather than a proportion of the width, so
    /// a selection dragged into the box lands between two characters.
    /// </summary>
    public override float GetSelectionOffset(SKPoint pointInLocal) =>
        MultilineFeature is { } multiline
            ? multiline.GetIndexFromPoint(this, Viewport, pointInLocal)
            : IndexFromX(pointInLocal.X);

    /// <summary>
    /// Adds the run of characters covered by the selection, using the same geometry as the box's
    /// own highlight so a control-level selection looks identical to an internal one.
    /// </summary>
    public override void GetSelectionRects(int start, int length, List<SKRect> output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (MultilineFeature is { } multiline)
        {
            multiline.GetSelectionRects(this, Viewport, start, length, output);
            return;
        }

        var content = TextRect;
        if (length <= 0 || content.Width <= 0f || content.Height <= 0f)
        {
            return;
        }

        var display = DisplayText;
        float lineHeight = _font.LineHeight;
        float top = content.Top + MathF.Max(0f, (content.Height - lineHeight) / 2f);
        float originX = content.Left - _scrollX;

        float left = MathF.Max(originX + MeasureX(display, Math.Clamp(start, 0, display.Length)), content.Left);
        float right = MathF.Min(originX + MeasureX(display, Math.Clamp(start + length, 0, display.Length)), content.Right);
        if (right > left)
        {
            output.Add(new SKRect(left, top, right, top + lineHeight));
        }
    }

    /// <summary>
    /// Records which annotated span the press landed in before any handler sees the event, so the
    /// answer is the one the frame showed rather than a hit test re-run later, when the field may
    /// have been scrolled or re-wrapped underneath the pointer. A press outside every span, and a
    /// press routed up from a child control, both record null.
    /// </summary>
    protected internal override bool RaiseContextMenuOpening(XPlatContextMenuEventArgs args)
    {
        if (SpanFeature is { } spans)
        {
            if (ReferenceEquals(args.Control, this))
            {
                var origin = GetPositionInHost();
                args.Hit = spans.HitTest(this, new SKPoint(args.Location.X - origin.X, args.Location.Y - origin.Y));
            }
            else
            {
                args.Hit = null;
            }
        }

        return base.RaiseContextMenuOpening(args);
    }

    /// <summary>
    /// Offers the editing commands the box supports - Cut, Copy, Paste and Select All - instead
    /// of the generic menu. Copy falls back to the host's selection when the box is only part of
    /// a larger selection, which is what the generic menu would have offered.
    /// </summary>
    /// <remarks>
    /// This is what a right-click over the box starts with: a handler adds its own items to it, so
    /// an annotation menu sits beside the editing commands rather than replacing them. Each command
    /// is enabled by the rule the command itself obeys - nothing selected means no Cut or Copy, a
    /// read-only box means no Cut or Paste, an empty clipboard means no Paste, and empty text means
    /// no Select All - so a disabled row here and a call that would have done nothing agree.
    /// </remarks>
    protected internal override XPlatContextMenu? BuildContextMenu()
    {
        if (ContextMenu is not null || !Enabled)
        {
            return base.BuildContextMenu();
        }

        bool hasSelection = HasSelection;
        bool canWrite = !ReadOnly;
        bool canPaste = canWrite && !string.IsNullOrEmpty(XPlatClipboard.GetText());

        return new XPlatContextMenu()
            .Add(new XPlatMenuItem("Cut", () => Cut())
            {
                Shortcut = new XPlatShortcut(XPlatKey.X),
                Enabled = canWrite && hasSelection,
            })
            .Add(new XPlatMenuItem("Copy", CopySelection)
            {
                Shortcut = new XPlatShortcut(XPlatKey.C),
                Enabled = hasSelection || IsSelected,
            })
            .Add(new XPlatMenuItem("Paste", () => Paste())
            {
                Shortcut = new XPlatShortcut(XPlatKey.V),
                Enabled = canPaste,
            })
            .AddSeparator()
            .Add(new XPlatMenuItem("Select All", SelectAll)
            {
                Shortcut = new XPlatShortcut(XPlatKey.A),
                Enabled = Text.Length > 0,
            });
    }

    /// <summary>The border takes the hover colour, so the box has to repaint when the pointer arrives or leaves.</summary>
    protected override void OnMouseEnter()
    {
        base.OnMouseEnter();
        Invalidate();
    }

    /// <summary>The border takes the hover colour, so the box has to repaint when the pointer arrives or leaves.</summary>
    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        Invalidate();
    }

    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        var fill = Enabled ? BackColor ?? XPlatTheme.InputBackground : XPlatTheme.DisabledBackground;
        XPlatPainter.Fill(canvas, rect, fill, CornerRadius);

        var border = !Enabled
            ? XPlatTheme.DisabledBackground
            : IsFocused
                ? XPlatTheme.Accent
                : IsMouseOver ? XPlatTheme.SplitterHover : BorderColor;

        XPlatPainter.DrawBorder(canvas, rect, border, BorderWidth > 0f ? BorderWidth : 1f, CornerRadius);
    }

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        if (MultilineFeature is { } multiline)
        {
            // The feature lays the text out and paints it; everything that has to sit under or over
            // the glyphs is drawn here, so the order is the same on a one-line field.
            var area = Viewport.Text;
            PaintUnderText(canvas, area);
            multiline.Paint(this, canvas, Viewport, _placeholderText);
            PaintCaret(canvas, area);
            return;
        }

        var content = XPlatGeometry.Deflate(rect, ContentPadding);
        if (content.Width <= 0f || content.Height <= 0f)
        {
            return;
        }

        PaintUnderText(canvas, content);

        var display = DisplayText;
        float lineHeight = _font.LineHeight;
        float top = content.Top + MathF.Max(0f, (content.Height - lineHeight) / 2f);
        float baseline = top + _font.BaselineOffset;
        float originX = content.Left - _scrollX;

        canvas.Save();
        canvas.ClipRect(content);

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        string text = display;
        var color = ForeColor;
        if (text.Length == 0 && !string.IsNullOrEmpty(_placeholderText))
        {
            text = _placeholderText;
            color = XPlatTheme.Placeholder;
        }

        if (!Enabled)
        {
            color = XPlatTheme.DisabledForeground;
        }

        if (text.Length > 0)
        {
            paint.Color = color;
            canvas.DrawText(text, originX, baseline, SKTextAlign.Left, _font.SkFont, paint);
        }

        canvas.Restore();

        PaintCaret(canvas, content);
    }

    /// <summary>
    /// Paints what a marker has to be drawn under or over: the selection highlight, then the
    /// annotation squiggles, both clipped to the area the text is laid out in.
    /// </summary>
    /// <remarks>
    /// The order the paint path uses is selection, squiggles, text, caret, on a one-line and a
    /// multi-line field alike. The wash goes <em>under</em> the squiggle because the theme's
    /// selection colour is caller-supplied and may be opaque, which would otherwise hide a marked
    /// word exactly while the user is looking at it; a squiggle in its own colour survives the
    /// translucent wash that is the usual case and stays legible under an opaque one. The glyphs go
    /// over the squiggle so a descender is drawn on top of it instead of being cut by it, and the
    /// caret goes last so a blinking caret is never covered by a marker.
    /// </remarks>
    private void PaintUnderText(SKCanvas canvas, SKRect area)
    {
        if (area.Width <= 0f || area.Height <= 0f)
        {
            return;
        }

        canvas.Save();
        canvas.ClipRect(area);

        if (HasSelection && Enabled && SelectionLength > 0)
        {
            var rects = new List<SKRect>();
            GetSelectionRects(SelectionStart, SelectionLength, rects);
            if (rects.Count > 0)
            {
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = XPlatTheme.Selection,
                };

                foreach (var selection in rects)
                {
                    canvas.DrawRect(selection, paint);
                }
            }
        }

        SpanFeature?.Paint(this, canvas);
        canvas.Restore();
    }

    /// <summary>
    /// Draws the caret, clipped to the area the text is laid out in. Last of all, so a squiggle or
    /// a glyph can never cover it.
    /// </summary>
    private void PaintCaret(SKCanvas canvas, SKRect area)
    {
        if (area.Width <= 0f || area.Height <= 0f)
        {
            return;
        }

        canvas.Save();
        canvas.ClipRect(area);
        _caret.Draw(canvas, CaretBounds);
        canvas.Restore();
    }

    protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        // The border is drawn in OnPaintBackground so it stays behind the text and caret.
    }

    protected override void OnMouseDown(XPlatMouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != XPlatMouseButtons.Left || !Enabled)
        {
            return;
        }

        ActivateCaret();
        Focus();
        _selecting = true;
        CaptureMouse();

        int index = MultilineFeature is { } multiline
            ? multiline.GetIndexFromPoint(this, Viewport, e.Location)
            : IndexFromX(e.Location.X);
        if (e.ClickCount >= 3)
        {
            SelectAll();
        }
        else if (e.ClickCount == 2)
        {
            var (start, length) = WordAt(index);
            Select(start, length);
        }
        else
        {
            _anchor = _caretIndex = index;
            _caret.ResetBlink();
            EnsureCaretVisible();
            Invalidate();
        }
    }

    protected override void OnMouseMove(XPlatMouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_selecting)
        {
            MoveCaretTo(
                MultilineFeature is { } multiline
                    ? multiline.GetIndexFromPoint(this, Viewport, e.Location)
                    : IndexFromX(e.Location.X),
                extendSelection: true);
        }
    }

    protected override void OnMouseUp(XPlatMouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (!_selecting)
        {
            return;
        }

        _selecting = false;
        ReleaseMouseCapture();
        Invalidate();
    }

    protected override void OnKeyDown(XPlatKeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        bool shift = (e.Modifiers & XPlatModifiers.Shift) != 0;
        bool control = (e.Modifiers & XPlatModifiers.Control) != 0;

        if (MultilineFeature is { } multiline && multiline.OnKeyDown(this, Viewport, e))
        {
            return;
        }

        switch (e.Key)
        {
            case XPlatKey.Left:
                MoveCaretTo(control ? PreviousWordIndex(_caretIndex) : _caretIndex - 1, shift);
                e.Handled = true;
                break;

            case XPlatKey.Right:
                MoveCaretTo(control ? NextWordIndex(_caretIndex) : _caretIndex + 1, shift);
                e.Handled = true;
                break;

            case XPlatKey.Home:
                MoveCaretTo(0, shift);
                e.Handled = true;
                break;

            case XPlatKey.End:
                MoveCaretTo(Text.Length, shift);
                e.Handled = true;
                break;

            case XPlatKey.Backspace:
                e.Handled = true;
                if (ReadOnly)
                {
                    break;
                }

                if (HasSelection)
                {
                    ReplaceRange(SelectionStart, SelectionLength, string.Empty);
                }
                else if (_caretIndex > 0)
                {
                    ReplaceRange(_caretIndex - 1, 1, string.Empty);
                }

                break;

            case XPlatKey.Delete:
                e.Handled = true;
                if (ReadOnly)
                {
                    break;
                }

                if (HasSelection)
                {
                    ReplaceRange(SelectionStart, SelectionLength, string.Empty);
                }
                else if (_caretIndex < Text.Length)
                {
                    ReplaceRange(_caretIndex, 1, string.Empty);
                }

                break;

            case XPlatKey.A when control:
                SelectAll();
                e.Handled = true;
                break;

            case XPlatKey.C when control:
                Copy();
                e.Handled = true;
                break;

            case XPlatKey.X when control:
                Cut();
                e.Handled = true;
                break;

            case XPlatKey.V when control:
                Paste();
                e.Handled = true;
                break;
        }
    }

    protected override void OnTextInput(XPlatTextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (e.Handled || ReadOnly)
        {
            return;
        }

        var text = Sanitize(e.Text);
        if (text.Length == 0)
        {
            return;
        }

        InsertText(text);
        e.Handled = true;
    }

    protected override void OnTextChanged()
    {
        _caretIndex = Math.Clamp(_caretIndex, 0, Text.Length);
        _anchor = Math.Clamp(_anchor, 0, Text.Length);

        MultilineFeature?.OnTextChanged(this, Viewport);

        ClampScroll();

        base.OnTextChanged();
    }

    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        ActivateCaret();
        EnsureCaretVisible();
        Invalidate();
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        _selecting = false;
        Invalidate();
    }

    protected override void OnSelectionCleared(EventArgs e)
    {
        base.OnSelectionCleared(e);
        _anchor = _caretIndex;

        // The host dropping the selection is not the box giving up the insertion point. While the
        // focus is still here the caret keeps blinking, so a press that never moved the focus
        // cannot leave a focused box looking as if it had lost it.
        if (!IsFocused)
        {
            _caret.Deactivate();
        }
    }

    protected override void OnBoundsChanged()
    {
        base.OnBoundsChanged();

        // Layout has already run, so the scroll helper knows the new viewport and the caret can be
        // brought back inside it.
        EnsureCaretVisible();
    }

    /// <summary>Positions the scrollbars, then the text they scroll. A single-line box has neither.</summary>
    protected override void OnLayout()
    {
        base.OnLayout();
        MultilineFeature?.Layout(this, Viewport);
    }

    /// <summary>
    /// The scrollbars belong to the box but are not children of it: <see cref="XPlatScrollBars"/>
    /// positions them itself and the container paints them last, exactly as <see cref="XPlatPanel"/>
    /// does with its own.
    /// </summary>
    protected override void RenderChildren(SKCanvas canvas)
    {
        base.RenderChildren(canvas);
        MultilineFeature?.RenderBars(canvas);
    }

    /// <summary>Lets a press land on a scrollbar instead of putting the caret where the thumb is.</summary>
    protected override XPlatControl? HitTestChildren(SKPoint local) =>
        MultilineFeature?.HitTestBar(local) ?? base.HitTestChildren(local);

    /// <summary>
    /// Turns the wheel over the text. The feature claims the turn only when it moved, so a box
    /// already at its end hands the rest of the turn to the container around it.
    /// </summary>
    protected override void OnMouseWheel(XPlatMouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (!e.Handled && MultilineFeature is { } multiline)
        {
            e.Handled = multiline.OnMouseWheel(this, e);
        }
    }

    protected override void OnEnabledChanged()
    {
        if (!Enabled)
        {
            _selecting = false;
            ReleaseMouseCapture();
        }

        base.OnEnabledChanged();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SpanFeature?.Dispose();
            SpanFeature = null;
            MultilineFeature?.Dispose();
            MultilineFeature = null;
        }

        base.Dispose(disposing);
    }

    private string DisplayText =>
        _isPassword && Text.Length > 0 ? new string(_passwordChar, Text.Length) : Text;

    private void ReplaceRange(int start, int length, string replacement)
    {
        if (ReadOnly)
        {
            return;
        }

        ActivateCaret();

        start = Math.Clamp(start, 0, Text.Length);
        length = Math.Clamp(length, 0, Text.Length - start);

        if (length == 0 && replacement.Length == 0)
        {
            return;
        }

        if (_maxLength > 0)
        {
            int room = _maxLength - (Text.Length - length);
            if (room <= 0)
            {
                return;
            }

            if (replacement.Length > room)
            {
                replacement = replacement[..room];
            }
        }

        _anchor = _caretIndex = start + replacement.Length;
        Text = Text.Remove(start, length).Insert(start, replacement);

        _caret.ResetBlink();
        EnsureCaretVisible();
        Invalidate();
    }

    private void MoveCaretTo(int index, bool extendSelection)
    {
        ActivateCaret();

        _caretIndex = Math.Clamp(index, 0, Text.Length);
        if (!extendSelection)
        {
            _anchor = _caretIndex;
        }

        _caret.ResetBlink();
        EnsureCaretVisible();
        Invalidate();
    }

    /// <summary>
    /// Takes the caret and tells the host that this text box is taking over the selection, so any
    /// selection elsewhere in the UI is cleared first.
    /// </summary>
    private void ActivateCaret()
    {
        _caret.Activate();
        ClaimSelection();
    }

    private void EnsureCaretVisible()
    {
        if (MultilineFeature is { } multiline)
        {
            multiline.EnsureCaretVisible(this, Viewport);
            return;
        }

        float width = TextRect.Width;
        if (width <= 0f)
        {
            _scrollX = 0f;
            return;
        }

        float caretX = MeasureX(DisplayText, _caretIndex);
        if (caretX - _scrollX > width - HorizontalCaretPadding)
        {
            _scrollX = caretX - width + HorizontalCaretPadding;
        }
        else if (caretX - _scrollX < 0f)
        {
            _scrollX = caretX;
        }

        ClampScroll();
    }

    private void ClampScroll()
    {
        if (MultilineFeature is { } multiline)
        {
            multiline.ClampScroll(this, Viewport);
            return;
        }

        float width = TextRect.Width;
        float textWidth = _font.MeasureWidth(DisplayText);
        float max = MathF.Max(0f, textWidth - width + HorizontalCaretPadding + _caret.Width);
        _scrollX = Math.Clamp(_scrollX, 0f, max);
    }

    private float MeasureX(string display, int index)
    {
        if (index <= 0 || display.Length == 0)
        {
            return 0f;
        }

        return _font.MeasureWidth(index >= display.Length ? display : display[..index]);
    }

    private int IndexFromX(float xInControl)
    {
        var display = DisplayText;
        if (display.Length == 0)
        {
            return 0;
        }

        float target = xInControl - TextRect.Left + _scrollX;
        if (target <= 0f)
        {
            return 0;
        }

        float best = float.MaxValue;
        int bestIndex = 0;
        for (int i = 0; i <= display.Length; i++)
        {
            float distance = MathF.Abs(MeasureX(display, i) - target);
            if (distance < best)
            {
                best = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private int PreviousWordIndex(int index)
    {
        var text = Text;
        int i = Math.Clamp(index, 0, text.Length);
        while (i > 0 && !IsWordChar(text[i - 1]))
        {
            i--;
        }

        while (i > 0 && IsWordChar(text[i - 1]))
        {
            i--;
        }

        return i;
    }

    private int NextWordIndex(int index)
    {
        var text = Text;
        int i = Math.Clamp(index, 0, text.Length);
        while (i < text.Length && IsWordChar(text[i]))
        {
            i++;
        }

        while (i < text.Length && !IsWordChar(text[i]))
        {
            i++;
        }

        return i;
    }

    private (int Start, int Length) WordAt(int index)
    {
        var text = Text;
        if (text.Length == 0)
        {
            return (0, 0);
        }

        int i = Math.Clamp(index, 0, text.Length - 1);
        int start = i;
        int end = i + 1;

        if (IsWordChar(text[i]))
        {
            while (start > 0 && IsWordChar(text[start - 1]))
            {
                start--;
            }

            while (end < text.Length && IsWordChar(text[end]))
            {
                end++;
            }
        }
        else
        {
            while (start > 0 && !IsWordChar(text[start - 1]))
            {
                start--;
            }

            while (end < text.Length && !IsWordChar(text[end]))
            {
                end++;
            }
        }

        return (start, end - start);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Prepares text for the box: a multi-line box keeps its line breaks and normalises them, a
    /// single-line box strips every control character so the field stays one line.
    /// </summary>
    private string Sanitize(string? text) =>
        MultilineFeature is { } multiline
            ? multiline.SanitizeText(text ?? string.Empty)
            : SanitizeSingleLine(text);

    /// <summary>Strips control characters (including line breaks) so the field stays single-line.</summary>
    internal static string SanitizeSingleLine(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (!char.IsControl(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}

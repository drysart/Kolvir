using System.Text;
using SkiaSharp;
using Kolvir.Multiline;
using Kolvir.Spans;
using Kolvir.Undo;

namespace Kolvir;

/// <summary>
/// A single-line editable text field with a blinking caret, selection, clipboard support,
/// password masking and placeholder text. Like every Kolvir control it has no platform
/// dependency: it paints into an <see cref="SKCanvas"/> and receives mouse, keyboard and
/// text input from an <see cref="UiHost"/>.
/// </summary>
/// <remarks>
/// The caret is an <see cref="Caret"/>, so it looks and blinks like the caret of every other
/// editable control and only runs a timer while this text box has focus and is displayed. Adapters
/// only need to install a platform clipboard into <see cref="Clipboard.Current"/>.
/// </remarks>
public class TextBox : Control
{
    private const float HorizontalCaretPadding = 2f;

    private readonly Caret _caret;
    private string _placeholderText = string.Empty;
    private int _caretIndex;
    private int _anchor;
    private float _scrollX;
    private bool _selecting;
    private int _maxLength;
    private bool _readOnly;
    private bool _isPassword;
    private char _passwordChar = '\u2022';

    /// <summary>
    /// True while <see cref="ReplaceRange"/> is assigning <see cref="Text"/> on the user's behalf,
    /// so the setter knows the change is already recorded as that edit rather than as a
    /// programmatic assignment.
    /// </summary>
    private bool _editingRange;

    /// <summary>
    /// Creates an empty single-line field: focusable, with an I-beam cursor and a caret, a one-pixel
    /// rounded border, eight pixels of horizontal and six of vertical padding, and a minimum height
    /// of 30 pixels.
    /// </summary>
    public TextBox()
    {
        TabStop = true;
        Cursor = Cursor.IBeam;
        _caret = new Caret(this);
        BorderWidth = 1f;
        CornerRadius = 6f;
        Padding = new Thickness(8f, 6f);
        MinimumSize = new SKSize(0f, 30f);

        // A text box is always part of a selection: its own caret and anchor answer for a drag
        // that starts inside it, and the control-level contract answers for a selection that
        // merely passes through it. The two never fight - claiming a selection is exclusive.
        Selectable = true;
        SelectionStyle = SelectionStyle.Partial;
    }

    /// <summary>Creates a field holding <paramref name="text"/>.</summary>
    /// <param name="text">The initial text, normalised by the same rules typing follows.</param>
    public TextBox(string text)
        : this()
    {
        Text = text;
    }

    /// <summary>An editable field is painted on the input fill until <see cref="Control.Background"/> is assigned.</summary>
    protected override Background DefaultBackground => Background.FromThemeColor(ThemeColor.InputBackground);

    /// <summary>
    /// The multi-line feature when this box opted into it, otherwise null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole seam between the text box and <c>Kolvir.Multiline</c>, and the one thing
    /// every text box in every application carries for a feature it may never use: a null reference
    /// and a null check. Everything the feature does - measuring, painting, hit testing, the
    /// scrollbars, the keys that mean something else once text spans lines - is called through this
    /// interface, and the interface's only implementation is created by the extension property that
    /// turns the feature on. An application that never sets <c>Multiline</c> therefore publishes
    /// without the feature's code, which is what FEATURE-COST-POLICY.md §4's first rung asks for and
    /// what a field or a member per multi-line concern could not deliver.
    /// </para>
    /// </remarks>
    internal ITextBoxMultiline? MultilineFeature { get; set; }

    /// <summary>
    /// The span annotation feature when this box has spans, otherwise null. The second of the text
    /// box's two nullable seams, and the same shape as <see cref="MultilineFeature"/>: drawing a
    /// squiggle is the control's own paint path, so it cannot live outside the control, while every
    /// type that knows how to draw one is named only by the extension property that installs this.
    /// A box that never assigns <c>Spans</c> therefore carries the reference and a null check on the
    /// paint path and publishes without the rest.
    /// </summary>
    internal ITextBoxSpanFeature? SpanFeature { get; set; }

    /// <summary>
    /// The undo history when this box opted into it, otherwise null. The third of the text box's
    /// nullable seams, and the same shape as the other two: every edit the control makes funnels
    /// through <see cref="ReplaceRange"/>, and a programmatic assignment through the
    /// <see cref="Text"/> setter, so those are the only places that have to ask the history what to
    /// record. The entries, the coalescing rule and the depth cap are named only by the extension
    /// property that installs this, so a box that never turns <c>UndoEnabled</c> on carries the
    /// reference and the null checks and publishes without the rest.
    /// </summary>
    internal ITextBoxUndoFeature? UndoFeature { get; set; }

    /// <summary>True when the box edits several lines. Answers from the seam, so it costs one null check.</summary>
    internal bool IsMultiline => MultilineFeature is not null;

    /// <summary>The client area and padding the multi-line feature lays text out against.</summary>
    internal TextBoxViewport Viewport => new(ClientRect, ContentPadding);

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
            var sanitized = MultilineFeature?.SanitizeText(text) ?? text;

            // A programmatic assignment is a change the history records as one entry, so a document
            // loaded into the field can be taken back out. An assignment of the value the box
            // already holds never gets here - the base setter ignores it and so does this - which is
            // what keeps a two-way binding that echoes each keystroke back out of the history.
            if (_editingRange || UndoFeature is not { } undo || string.Equals(Text, sanitized, StringComparison.Ordinal))
            {
                base.Text = sanitized;
                ClampSelectionToText();
                return;
            }

            undo.BeginAssign(this, Text);
            base.Text = sanitized;

            // The entry records where the assignment left the insertion point, so the clamp has to
            // happen before the history is told about it - otherwise a redo would put the caret back
            // where it was and drop a selection the new value still has room for.
            ClampSelectionToText();
            undo.EndEdit(this);
        }
    }

    /// <summary>
    /// Keeps the insertion point and the selection inside the value the box now holds. An
    /// assignment that gives the box shorter text takes the caret and the anchor with it instead of
    /// leaving either past the end, which is what a native field does and what stops an updated
    /// field from appearing to jump. Both indices count the real text, so a multi-line value clamps
    /// against its whole length and a password field against the characters it masks.
    /// </summary>
    private void ClampSelectionToText()
    {
        _caretIndex = Math.Clamp(_caretIndex, 0, Text.Length);
        _anchor = Math.Clamp(_anchor, 0, Text.Length);
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
    /// their own chrome in the field, such as <see cref="NumericUpDown"/>'s spinner, widen
    /// it so the text stops short of what they draw instead of running underneath it.
    /// </summary>
    protected virtual Thickness ContentPadding => Padding;

    /// <summary>The area the text is laid out in: the client rect without <see cref="ContentPadding"/>.</summary>
    private protected SKRect TextRect => Geometry.Deflate(ClientRect, ContentPadding);

    /// <summary>
    /// Text at another size takes other room, so the horizontal scroll offset is clamped to what
    /// the text now needs - a font that shrank must not leave the field scrolled past its end.
    /// </summary>
    protected override void OnAppearanceChanged() => ClampScroll();

    /// <summary>The insertion point's caret, which this text box owns and positions.</summary>
    public Caret Caret => _caret;

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

            float lineHeight = Font.LineHeight;
            float top = content.Top + MathF.Max(0f, (content.Height - lineHeight) / 2f);
            float x = content.Left - _scrollX + MeasureX(DisplayText, _caretIndex);
            return new SKRect(x, top, x + _caret.Width, top + lineHeight);
        }
    }

    /// <summary>Index of the insertion point (0 .. <see cref="Control.Text"/>.Length).</summary>
    public int CaretIndex
    {
        get => _caretIndex;
        set => MoveCaretTo(value, extendSelection: false);
    }

    /// <summary>Lower bound of the selection; equals <see cref="CaretIndex"/> when nothing is selected.</summary>
    public int SelectionStart => Math.Min(_caretIndex, _anchor);

    /// <summary>How many characters are selected; zero when the selection is collapsed.</summary>
    public int SelectionLength => Math.Abs(_caretIndex - _anchor);

    /// <summary>True when more than a collapsed caret is selected.</summary>
    public bool HasSelection => _caretIndex != _anchor;

    /// <summary>
    /// The fixed end of the selection, which is where a right-to-left drag started. It is separate
    /// from <see cref="CaretIndex"/> so a selection has a direction; the undo history records it
    /// with each entry so undo can restore a selection the way it was made.
    /// </summary>
    internal int SelectionAnchor => _anchor;

    /// <summary>The selected text, or the empty string when nothing is selected.</summary>
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

    /// <summary>Muted text shown while <see cref="Control.Text"/> is empty.</summary>
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

    /// <summary>Selects the whole value, leaving the caret at its end.</summary>
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

        Clipboard.SetText(SelectedText);
        return true;
    }

    /// <summary>Copies the selection to the clipboard and deletes it. Returns false when nothing is selected.</summary>
    public bool Cut()
    {
        if (!HasSelection || ReadOnly)
        {
            return false;
        }

        Clipboard.SetText(SelectedText);
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

        var text = Sanitize(Clipboard.GetText());
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

    /// <inheritdoc />
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        if (MultilineFeature is { } multiline)
        {
            return multiline.Measure(this, Viewport, availableSize);
        }

        var sample = Text.Length == 0 ? "W" : Text;
        float width = MathF.Min(MathF.Ceiling(Font.MeasureWidth(sample)) + 4f, 320f);
        return new SKSize(width, MathF.Ceiling(Font.LineHeight) + 2f);
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
        float lineHeight = Font.LineHeight;
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
    protected internal override bool RaiseContextMenuOpening(ContextMenuEventArgs args)
    {
        if (SpanFeature is { } spans)
        {
            if (ReferenceEquals(args.Control, this))
            {
                // The event carries a host-space point; the spans live in this control's own
                // space, which a zoomed ancestor has scaled.
                args.Hit = spans.HitTest(this, HostToClient(args.Location));
            }
            else
            {
                args.Hit = null;
            }
        }

        return base.RaiseContextMenuOpening(args);
    }

    /// <summary>
    /// Offers the editing commands the box supports - Undo and Redo when the box has a history, then
    /// Cut, Copy, Paste and Select All - instead of the generic menu. Copy falls back to the host's
    /// selection when the box is only part of a larger selection, which is what the generic menu
    /// would have offered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what a right-click over the box starts with: a handler adds its own items to it, so
    /// an annotation menu sits beside the editing commands rather than replacing them. Each command
    /// is enabled by the rule the command itself obeys - nothing selected means no Cut or Copy, a
    /// read-only box means no Cut or Paste, an empty clipboard means no Paste, and empty text means
    /// no Select All - so a disabled row here and a call that would have done nothing agree.
    /// </para>
    /// <para>
    /// Undo and Redo are contributed only by a box that has the history at all, which is a box the
    /// application opted into with <c>UndoEnabled</c>. A box without one has nothing to undo and
    /// nothing that could ever light the rows up, so it gets no rows for them: the history is
    /// opt-in, and a feature that was never turned on should not take up space in a menu the
    /// consumer did not design around it. A box that <em>has</em> the history but cannot use it at
    /// this moment - a read-only field, or an empty stack - still shows both rows, disabled, so the
    /// rows do not appear and disappear at the user as the stack changes; their enabled state is
    /// <c>CanUndo</c> and <c>CanRedo</c> as the menu is built.
    /// </para>
    /// <para>
    /// The rows run the same operation the keyboard does, so a click and a key press are the same
    /// edit, and both put the caret back where the entry says it was.
    /// </para>
    /// </remarks>
    protected internal override ContextMenu? BuildContextMenu()
    {
        if (ContextMenu is not null || !Enabled)
        {
            return base.BuildContextMenu();
        }

        bool hasSelection = HasSelection;
        bool canWrite = !ReadOnly;
        bool canPaste = canWrite && !string.IsNullOrEmpty(Clipboard.GetText());

        var menu = new ContextMenu();

        if (UndoFeature is { } undo)
        {
            menu.Add(new MenuItem("Undo", () => undo.Undo(this))
                {
                    Shortcut = new Shortcut(Key.Z),
                    Enabled = canWrite && undo.CanUndo,
                })
                .Add(new MenuItem("Redo", () => undo.Redo(this))
                {
                    Shortcut = new Shortcut(Key.Z, Modifiers.Control | Modifiers.Shift),
                    Enabled = canWrite && undo.CanRedo,
                })
                .AddSeparator();
        }

        return menu
            .Add(new MenuItem("Cut", () => Cut())
            {
                Shortcut = new Shortcut(Key.X),
                Enabled = canWrite && hasSelection,
            })
            .Add(new MenuItem("Copy", CopySelection)
            {
                Shortcut = new Shortcut(Key.C),
                Enabled = hasSelection || IsSelected,
            })
            .Add(new MenuItem("Paste", () => Paste())
            {
                Shortcut = new Shortcut(Key.V),
                Enabled = canPaste,
            })
            .AddSeparator()
            .Add(new MenuItem("Select All", SelectAll)
            {
                Shortcut = new Shortcut(Key.A),
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

    /// <summary>
    /// Fills the field and draws its border: disabled, focused, hovered and resting states each have
    /// their own colour, and the border is drawn here rather than in the border pass so the text and
    /// the caret are painted over it.
    /// </summary>
    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        var fill = Enabled ? BackColor ?? Theme.InputBackground : Theme.DisabledBackground;
        Painter.Fill(canvas, rect, fill, CornerRadius);

        var border = !Enabled
            ? Theme.DisabledBackground
            : IsFocused
                ? Theme.Accent
                : IsMouseOver ? Theme.SplitterHover : BorderColor;

        Painter.DrawBorder(canvas, rect, border, BorderWidth > 0f ? BorderWidth : 1f, CornerRadius);
    }

    /// <summary>
    /// Paints the selection, the text and the caret in that order - the placeholder while the value
    /// is empty, the mask instead of the real characters when <see cref="IsPassword"/> is set, and
    /// the multi-line feature's own layout when the box has one.
    /// </summary>
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

        var content = Geometry.Deflate(rect, ContentPadding);
        if (content.Width <= 0f || content.Height <= 0f)
        {
            return;
        }

        PaintUnderText(canvas, content);

        var display = DisplayText;
        float lineHeight = Font.LineHeight;
        float top = content.Top + MathF.Max(0f, (content.Height - lineHeight) / 2f);
        float baseline = top + Font.BaselineOffset;
        float originX = content.Left - _scrollX;

        canvas.Save();
        canvas.ClipRect(content);

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        string text = display;
        var color = ForeColor;
        if (text.Length == 0 && !string.IsNullOrEmpty(_placeholderText))
        {
            text = _placeholderText;
            color = Theme.Placeholder;
        }

        if (!Enabled)
        {
            color = Theme.DisabledForeground;
        }

        if (text.Length > 0)
        {
            paint.Color = color;
            canvas.DrawText(text, originX, baseline, SKTextAlign.Left, Font.SkFont, paint);
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
                    Color = Theme.Selection,
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

    /// <summary>
    /// Nothing to draw: the border is painted in <see cref="OnPaintBackground"/> so that it stays
    /// behind the text and the caret.
    /// </summary>
    protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        // The border is drawn in OnPaintBackground so it stays behind the text and caret.
    }

    /// <summary>
    /// A left press on an enabled box takes the focus and the caret, starts a drag selection and
    /// captures the mouse. A double click selects the word under the pointer, a triple click the
    /// whole value, and a single click collapses the caret to the pressed character.
    /// </summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left || !Enabled)
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

    /// <summary>
    /// While a drag selection is in progress, moves the caret to the character under the pointer and
    /// extends the selection from its anchor.
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
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

    /// <summary>Ends the drag a press started, releasing the mouse capture and repainting.</summary>
    protected override void OnMouseUp(MouseEventArgs e)
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

    /// <summary>
    /// Handles the editing and navigation keys: the arrows, Home and End move the caret and extend
    /// the selection while Shift is held, Backspace and Delete edit, and Ctrl+A, C, X and V select
    /// all, copy, cut and paste. The undo history and the multi-line feature see the key first, so
    /// they can claim it before these decisions are made. Keys the box does not use are left
    /// unhandled.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        bool shift = (e.Modifiers & Modifiers.Shift) != 0;
        bool control = (e.Modifiers & Modifiers.Control) != 0;

        // The history sees the undo keys first, so neither the multi-line keys nor the editing
        // commands below can shadow Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z. Control is the library's
        // accelerator modifier; on macOS a Command chord never reaches the view at all, so the
        // AppKit backend's Edit menu feeds these same keys in through the host instead.
        if (UndoFeature is { } undo && undo.OnKeyDown(this, e))
        {
            return;
        }

        if (MultilineFeature is { } multiline && multiline.OnKeyDown(this, Viewport, e))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                MoveCaretTo(control ? PreviousWordIndex(_caretIndex) : _caretIndex - 1, shift);
                e.Handled = true;
                break;

            case Key.Right:
                MoveCaretTo(control ? NextWordIndex(_caretIndex) : _caretIndex + 1, shift);
                e.Handled = true;
                break;

            case Key.Home:
                MoveCaretTo(0, shift);
                e.Handled = true;
                break;

            case Key.End:
                MoveCaretTo(Text.Length, shift);
                e.Handled = true;
                break;

            case Key.Backspace:
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

            case Key.Delete:
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

            case Key.A when control:
                SelectAll();
                e.Handled = true;
                break;

            case Key.C when control:
                Copy();
                e.Handled = true;
                break;

            case Key.X when control:
                Cut();
                e.Handled = true;
                break;

            case Key.V when control:
                Paste();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Inserts typed text at the caret, replacing the selection. The text is sanitised first, so a
    /// single-line box drops control characters and a multi-line box normalises line breaks, and
    /// nothing is inserted into a read-only box or when the text is empty.
    /// </summary>
    protected override void OnTextInput(TextInputEventArgs e)
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

    /// <summary>
    /// Keeps the caret and the anchor inside the new value, lets the multi-line feature re-measure the
    /// text, clamps the scroll offset to what the text now needs, and then raises the text-changed
    /// event through the base.
    /// </summary>
    protected override void OnTextChanged()
    {
        ClampSelectionToText();

        MultilineFeature?.OnTextChanged(this, Viewport);

        ClampScroll();

        base.OnTextChanged();
    }

    /// <summary>Starts the caret blinking and scrolls it into view.</summary>
    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        ActivateCaret();
        EnsureCaretVisible();
        Invalidate();
    }

    /// <summary>
    /// Ends any drag in progress and repaints the now unfocused field, whose border and caret have
    /// changed.
    /// </summary>
    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        _selecting = false;
        Invalidate();
    }

    /// <summary>
    /// Collapses the selection to the caret when the host drops it. The caret keeps blinking while
    /// the box still has the focus, so a press that never moved the focus cannot leave a focused box
    /// looking as if it had lost it.
    /// </summary>
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

    /// <summary>
    /// Brings the caret back inside the new client area after the box has been resized.
    /// </summary>
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
    /// The scrollbars belong to the box but are not children of it: <see cref="ScrollBars"/>
    /// positions them itself and the container paints them last, exactly as <see cref="Panel"/>
    /// does with its own.
    /// </summary>
    protected override void RenderChildren(SKCanvas canvas)
    {
        base.RenderChildren(canvas);
        MultilineFeature?.RenderBars(canvas);
    }

    /// <summary>Lets a press land on a scrollbar instead of putting the caret where the thumb is.</summary>
    protected override Control? HitTestChildren(SKPoint local) =>
        MultilineFeature?.HitTestBar(local) ?? base.HitTestChildren(local);

    /// <summary>
    /// Turns the wheel over the text. The feature claims the turn only when it moved, so a box
    /// already at its end hands the rest of the turn to the container around it.
    /// </summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (!e.Handled && MultilineFeature is { } multiline)
        {
            e.Handled = multiline.OnMouseWheel(this, e);
        }
    }

    /// <summary>Abandons a drag and releases the mouse capture when the box is disabled.</summary>
    protected override void OnEnabledChanged()
    {
        if (!Enabled)
        {
            _selecting = false;
            ReleaseMouseCapture();
        }

        base.OnEnabledChanged();
    }

    /// <summary>
    /// Releases the undo history, the span annotations and the multi-line feature the box opted
    /// into, before the base control is disposed.
    /// </summary>
    /// <param name="disposing">True when the control is being disposed; false is never passed.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UndoFeature?.Dispose();
            UndoFeature = null;
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

        // The history is told before the caret and anchor move, so the entry keeps the insertion
        // point the user made the edit from. A non-zero selection makes the change an entry of its
        // own, which is why its length is part of the answer.
        UndoFeature?.BeginEdit(this, start, length, replacement, HasSelection ? SelectionLength : 0);

        _anchor = _caretIndex = start + replacement.Length;
        _editingRange = true;
        try
        {
            Text = Text.Remove(start, length).Insert(start, replacement);
        }
        finally
        {
            _editingRange = false;
        }

        _caret.ResetBlink();
        EnsureCaretVisible();
        Invalidate();
        UndoFeature?.EndEdit(this);
    }

    /// <summary>
    /// Applies one entry of the undo history verbatim: no <see cref="MaxLength"/> clamp, no
    /// read-only test and no recording, because the history has already decided what belongs in the
    /// text. Undoing a paste larger than the current limit has to restore it in full, and undoing
    /// anything at all must not appear in the history as a new edit.
    /// </summary>
    internal void ApplyHistoryEdit(int start, int length, string replacement)
    {
        start = Math.Clamp(start, 0, Text.Length);
        length = Math.Clamp(length, 0, Text.Length - start);

        _anchor = _caretIndex = start + replacement.Length;
        _editingRange = true;
        try
        {
            Text = Text.Remove(start, length).Insert(start, replacement);
        }
        finally
        {
            _editingRange = false;
        }

        _caret.ResetBlink();
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
        float textWidth = Font.MeasureWidth(DisplayText);
        float max = MathF.Max(0f, textWidth - width + HorizontalCaretPadding + _caret.Width);
        _scrollX = Math.Clamp(_scrollX, 0f, max);
    }

    private float MeasureX(string display, int index)
    {
        if (index <= 0 || display.Length == 0)
        {
            return 0f;
        }

        return Font.MeasureWidth(index >= display.Length ? display : display[..index]);
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

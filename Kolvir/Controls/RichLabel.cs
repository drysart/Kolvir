using System.Globalization;
using System.Text;
using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A label that renders rich text: a sequence of <see cref="TextSpan"/> items that each
/// carry their own formatting (bold, italic, underline, strikethrough, colour, size) and may
/// also place a live control in the text flow - the equivalent of CSS <c>inline-block</c>.
/// </summary>
/// <remarks>
/// <para>
/// Text wraps at the control's width (see <see cref="WrapText"/>) using a greedy line breaker
/// that mixes formatted runs and inline controls. All runs on a line share one baseline, so
/// differently sized text stays aligned; inline controls are aligned to that line's text box
/// using <see cref="TextSpan.InlineAlignment"/>.
/// </para>
/// <para>
/// Spans are not controls: they are lightweight objects owned by the label. A span raises
/// mouse events, which is what makes hyperlinks possible.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var label = new RichLabel { Dock = DockStyle.Top, WrapText = true };
/// label.AddText("Read the ");
/// label.AddLink("documentation", span => OpenDocs()).Tag = "docs";
/// label.AddText(" for details.");
/// label.AddInlineControl(new Button("Retry"), InlineAlignment.Center);
/// </code>
/// </example>
public class RichLabel : Control
{
    private Action<object, SpanEventArgs>? _spanClick;
    private enum FragmentKind
    {
        Word,
        Space,
        Break,
        Control,
    }

    private const float HighlightRadius = 2f;
    private const byte HoverBackAlpha = 0x1A;

    private readonly TextSpanCollection _spans;
    private readonly Dictionary<string, Font> _derivedFonts = new(StringComparer.Ordinal);
    private Layout? _layout;
    private TextSpan? _hoverSpan;
    private TextSpan? _pressedSpan;
    private bool _wrapText = true;
    private float _lineSpacing;

    /// <summary>
    /// Creates an empty label: it auto-sizes, wraps at the control's width and selects partially.
    /// </summary>
    public RichLabel()
    {
        _spans = new TextSpanCollection(this);
        AutoSize = true;
        SelectionStyle = SelectionStyle.Partial;
    }

    /// <summary>Creates a label from a set of spans.</summary>
    public RichLabel(params TextSpan[] spans)
        : this()
    {
        _spans.AddRange(spans);
    }

    /// <summary>Adds a handler that runs when a span is clicked (pressed and released over the same span).</summary>
    public IDisposable AddSpanClickHandler(Action<object, SpanEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _spanClick += handler;
        return new HandlerToken<Action<object, SpanEventArgs>>(RemoveSpanClickHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddSpanClickHandler"/> added.</summary>
    public void RemoveSpanClickHandler(Action<object, SpanEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _spanClick -= handler;
    }

    /// <summary>The formatted runs of this label, in flow order.</summary>
    public TextSpanCollection Spans => _spans;

    /// <summary>
    /// Spans derive their own font from the effective font, and the layout is built from those, so
    /// a font change - assigned here, inherited from an ancestor, or a new theme face - throws both
    /// away. The layout pass that follows re-tokenizes with the new font.
    /// </summary>
    protected override void OnAppearanceChanged()
    {
        ClearDerivedFonts();
        _layout = null;
    }

    /// <summary>When true (the default) lines break at the control's width.</summary>
    public bool WrapText
    {
        get => _wrapText;
        set
        {
            if (_wrapText == value)
            {
                return;
            }

            _wrapText = value;
            InvalidateLayout();
        }
    }

    /// <summary>Extra vertical space between two lines, in logical pixels.</summary>
    public float LineSpacing
    {
        get => _lineSpacing;
        set
        {
            value = MathF.Max(0f, value);
            if (MathF.Abs(_lineSpacing - value) < 0.01f)
            {
                return;
            }

            _lineSpacing = value;
            InvalidateLayout();
        }
    }

    /// <summary>Placement of the text block inside the content area.</summary>
    public ContentAlignment TextAlign { get; set; } = ContentAlignment.TopLeft;

    /// <summary>
    /// Concatenation of the span texts. Assigning replaces the spans with a single plain span.
    /// </summary>
    public override string Text
    {
        get
        {
            if (_spans.Count == 0)
            {
                return string.Empty;
            }

            if (_spans.Count == 1)
            {
                return _spans[0].Text;
            }

            var builder = new System.Text.StringBuilder();
            foreach (var span in _spans)
            {
                if (span.Control is null)
                {
                    builder.Append(span.Text);
                }
            }

            return builder.ToString();
        }

        set
        {
            var text = value ?? string.Empty;
            if (string.Equals(text, Text, StringComparison.Ordinal))
            {
                return;
            }

            _spans.Clear();
            if (text.Length > 0)
            {
                _spans.Add(new TextSpan(text));
            }

            OnTextChanged();
        }
    }

    /// <summary>Adds a plain span and returns it.</summary>
    public TextSpan AddText(string? text) => AddSpan(new TextSpan(text));

    /// <summary>
    /// Adds a span styled as a hyperlink: accent colour, underlined and a hand cursor.
    /// </summary>
    public TextSpan AddLink(string? text, Action<TextSpan>? onClick = null)
    {
        var span = new TextSpan(text)
        {
            ThemeColor = ThemeColor.Accent,
            Underline = true,
            HoverBackColorRole = ThemeColor.Accent,
            Cursor = Cursor.Hand,
        };

        if (onClick is not null)
        {
            span.AddClickHandler((_, e) => onClick(e.Span));
        }

        return AddSpan(span);
    }

    /// <summary>
    /// Adds a control that takes part in the text flow. The control becomes a child of this
    /// label and is positioned by the layout engine.
    /// </summary>
    public TextSpan AddInlineControl(Control control, InlineAlignment alignment = InlineAlignment.Bottom) =>
        AddSpan(TextSpan.ForControl(control, alignment));

    /// <summary>Adds an existing span and returns it.</summary>
    public TextSpan AddSpan(TextSpan span)
    {
        ArgumentNullException.ThrowIfNull(span);
        _spans.Add(span);
        return span;
    }

    /// <summary>Removes every span (and the inline controls they contributed).</summary>
    public void ClearSpans() => _spans.Clear();

    /// <inheritdoc />
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var layout = BuildLayout(NormalizeWrapWidth(availableSize.Width));
        return new SKSize(MathF.Ceiling(layout.Width), MathF.Ceiling(layout.Height));
    }

    /// <inheritdoc />
    protected override void OnLayout()
    {
        var content = ContentRect;
        if (content.Width <= 0f)
        {
            _layout = null;
            return;
        }

        var layout = BuildLayout(NormalizeWrapWidth(content.Width));
        Place(layout, content);
        ApplyInlineBounds(layout);
    }

    /// <inheritdoc />
    public override Cursor GetCursorAt(SKPoint pointInLocal)
    {
        var span = SpanAt(pointInLocal);
        if (span is not null && span.Cursor != Cursor.Default)
        {
            return span.Cursor;
        }

        return base.GetCursorAt(pointInLocal);
    }

    /// <summary>
    /// Paints every line: the hover and press washes first, then the glyphs, then the underlines and
    /// strike-throughs, all in the positions the layout gave them.
    /// </summary>
    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        var content = Geometry.Deflate(rect, Padding);
        if (content.Width <= 0f || content.Height <= 0f)
        {
            return;
        }

        var layout = EnsureLayout(content.Width);
        if (layout is null)
        {
            return;
        }

        Place(layout, content);

        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var textPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        foreach (var line in layout.Lines)
        {
            DrawSpanHighlights(canvas, line, fill);

            foreach (var fragment in line.Fragments)
            {
                if (fragment.Kind is FragmentKind.Control or FragmentKind.Space || fragment.Text.Length == 0)
                {
                    continue;
                }

                float baseline = fragment.Bounds.Top + line.Ascent;
                textPaint.Color = ResolveSpanColor(fragment.Span);
                canvas.DrawText(fragment.Text, fragment.Bounds.Left, baseline, SKTextAlign.Left, fragment.Font.SkFont, textPaint);
            }

            DrawSpanDecorations(canvas, line, fill);
        }
    }

    /// <summary>
    /// Moves the hover state to the span under the pointer - telling the span it left to raise its
    /// mouse-leave and the one it entered to raise its mouse-enter - and forwards the move to that
    /// span.
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var span = SpanAt(e.Location);
        if (!ReferenceEquals(span, _hoverSpan))
        {
            SetHoverSpan(span);
        }

        span?.RaiseMouseMove(new SpanEventArgs(span, e.Location, e.Button, e.Modifiers, e.ClickCount));
        base.OnMouseMove(e);
    }

    /// <summary>
    /// Presses the span under the pointer, if there is one, so it paints as pressed and its own
    /// mouse-down handlers run. Inline controls are excluded, because they receive the mouse
    /// themselves.
    /// </summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        var span = SpanAt(e.Location);
        if (span is not null)
        {
            span.IsPressed = true;
            _pressedSpan = span;
            span.RaiseMouseDown(new SpanEventArgs(span, e.Location, e.Button, e.Modifiers, e.ClickCount));
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    /// <summary>
    /// Releases the span that was pressed and, when the release lands on that same span, raises a
    /// click on it and then the label's own span-click event.
    /// </summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        var pressed = _pressedSpan;
        _pressedSpan = null;

        if (pressed is not null)
        {
            pressed.IsPressed = false;
            pressed.RaiseMouseUp(new SpanEventArgs(pressed, e.Location, e.Button, e.Modifiers, e.ClickCount));

            if (ReferenceEquals(pressed, SpanAt(e.Location)))
            {
                var args = new SpanEventArgs(pressed, e.Location, e.Button, e.Modifiers, e.ClickCount);
                pressed.RaiseClick(args);
                _spanClick?.Invoke(this, args);
            }

            Invalidate();
        }

        base.OnMouseUp(e);
    }

    /// <summary>
    /// Clears the hovered span, so its highlight goes away and its mouse-leave handler runs.
    /// </summary>
    protected override void OnMouseLeave()
    {
        SetHoverSpan(null);
        base.OnMouseLeave();
    }

    /// <summary>
    /// Clears the hovered and pressed spans when the label is disabled: a disabled control receives
    /// no mouse-leave, so a span hovered at the time would otherwise keep its highlight.
    /// </summary>
    protected override void OnEnabledChanged()
    {
        // Disabling a control does not send it a mouse-leave, so a span hovered or pressed at the
        // time would keep its highlight - and keep painting it, since nothing would clear it.
        if (!Enabled)
        {
            _pressedSpan = null;
            SetHoverSpan(null);
        }

        base.OnEnabledChanged();
    }

    internal override void OnControlAdded(Control control)
    {
        _layout = null;
        base.OnControlAdded(control);
    }

    internal override void OnControlRemoved(Control control)
    {
        _layout = null;
        base.OnControlRemoved(control);
    }

    /// <summary>Called when one of this label's spans changed and the layout must be rebuilt.</summary>
    internal void OnSpanChanged(TextSpan span)
    {
        if (ReferenceEquals(span, _hoverSpan) || ReferenceEquals(span, _pressedSpan))
        {
            SetHoverSpan(null);
        }

        InvalidateLayout();
    }

    internal void OnSpanAdded(TextSpan span)
    {
        if (span.Control is { } control && !ReferenceEquals(control.Parent, this))
        {
            Controls.Add(control);
        }

        InvalidateLayout();
    }

    internal void OnSpanRemoved(TextSpan span)
    {
        if (ReferenceEquals(span, _hoverSpan) || ReferenceEquals(span, _pressedSpan))
        {
            span.IsMouseOver = false;
            span.IsPressed = false;
            _hoverSpan = null;
            _pressedSpan = null;
        }

        if (span.Control is { } control && ReferenceEquals(control.Parent, this))
        {
            Controls.Remove(control);
        }

        InvalidateLayout();
    }

    /// <summary>
    /// Removes every span and releases the fonts derived for them before the base control is
    /// disposed.
    /// </summary>
    /// <param name="disposing">True when the control is being disposed; false is never passed.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _spans.Clear();
            ClearDerivedFonts();
            _layout = null;
        }

        base.Dispose(disposing);
    }

    private void InvalidateLayout()
    {
        _layout = null;
        PerformLayout();
        Invalidate();
    }

    private float NormalizeWrapWidth(float width) =>
        _wrapText && width > 0f && float.IsFinite(width) ? width : float.PositiveInfinity;

    private Layout? EnsureLayout(float width)
    {
        var wrapWidth = NormalizeWrapWidth(width);
        if (_layout is not null && _layout.WrapWidth.Equals(wrapWidth))
        {
            return _layout;
        }

        _layout = BuildLayout(wrapWidth);
        return _layout;
    }

    /// <summary>
    /// Returns the span whose glyphs contain the point (control spans are excluded, because the
    /// control itself receives the mouse), or null when the point is over empty space.
    /// </summary>
    public TextSpan? SpanAt(SKPoint pointInLocal)
    {
        var content = ContentRect;
        if (content.Width <= 0f || content.Height <= 0f)
        {
            return null;
        }

        var layout = EnsureLayout(content.Width);
        if (layout is null)
        {
            return null;
        }

        Place(layout, content);

        for (int i = layout.Lines.Count - 1; i >= 0; i--)
        {
            var line = layout.Lines[i];
            for (int j = line.Fragments.Count - 1; j >= 0; j--)
            {
                var fragment = line.Fragments[j];
                if (fragment.Kind != FragmentKind.Control && Geometry.Contains(fragment.Bounds, pointInLocal))
                {
                    return fragment.Span;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Maps a point to the nearest character boundary in <see cref="Control.Text"/>. The point
    /// snaps to the closest line first, so dragging above or below the text clamps to its ends.
    /// </summary>
    public override float GetSelectionOffset(SKPoint pointInLocal)
    {
        var layout = EnsurePlaced();
        if (layout is null)
        {
            return 0f;
        }

        Line? nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (var line in layout.Lines)
        {
            if (!TryGetLineBounds(line, out var bounds))
            {
                continue;
            }

            float distance = pointInLocal.Y < bounds.Top
                ? bounds.Top - pointInLocal.Y
                : pointInLocal.Y > bounds.Bottom
                    ? pointInLocal.Y - bounds.Bottom
                    : 0f;

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = line;
            }
        }

        return nearest is null ? 0f : OffsetInLine(nearest, pointInLocal.X);
    }

    /// <summary>
    /// Adds one rectangle per line for the selected text, plus the bounds of inline controls the
    /// selection reaches past on both sides.
    /// </summary>
    public override void GetSelectionRects(int start, int length, List<SKRect> output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (length <= 0)
        {
            return;
        }

        var layout = EnsurePlaced();
        if (layout is null)
        {
            return;
        }

        int end = start + length;

        foreach (var line in layout.Lines)
        {
            float runLeft = 0f;
            float runRight = 0f;
            SKRect runBounds = default;
            bool inRun = false;

            void Flush()
            {
                if (inRun)
                {
                    output.Add(new SKRect(runLeft, runBounds.Top, runRight, runBounds.Bottom));
                    inRun = false;
                }
            }

            foreach (var fragment in line.Fragments)
            {
                if (fragment.Kind == FragmentKind.Control)
                {
                    // A control carries no text, so it only counts as selected when the selection
                    // starts before it and ends after it.
                    Flush();
                    if (fragment.Start > start && fragment.Start < end)
                    {
                        output.Add(fragment.Bounds);
                    }

                    continue;
                }

                int from = Math.Max(start, fragment.Start) - fragment.Start;
                int to = Math.Min(end, fragment.Start + fragment.Text.Length) - fragment.Start;

                if (to <= from)
                {
                    Flush();
                    continue;
                }

                float left = fragment.Bounds.Left + MeasurePrefix(fragment, from);
                float right = fragment.Bounds.Left + MeasurePrefix(fragment, to);

                // Adjacent fragments on a line form one continuous highlight instead of one box
                // per word and space.
                if (inRun && MathF.Abs(left - runRight) <= 0.5f)
                {
                    runRight = MathF.Max(runRight, right);
                }
                else
                {
                    Flush();
                    runLeft = left;
                    runRight = right;
                    runBounds = fragment.Bounds;
                    inRun = true;
                }
            }

            Flush();
        }
    }

    /// <summary>
    /// The selected text with the tags its spans call for, so a copy carries the formatting:
    /// <c>&lt;b&gt;</c>, <c>&lt;i&gt;</c>, <c>&lt;u&gt;</c>, <c>&lt;s&gt;</c> and a
    /// <c>&lt;span style="..."&gt;</c> for colour and size overrides. A selection can start or
    /// end part way through a span, so only the selected slice of each span is wrapped; spans are
    /// contiguous in <see cref="Text"/>, which is exactly how the selection offsets are counted.
    /// Inline controls contribute no text and so no markup.
    /// </summary>
    public override string? GetSelectionHtml(int start, int length)
    {
        if (length <= 0)
        {
            return null;
        }

        var text = Text;
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);
        if (length <= 0)
        {
            return null;
        }

        int end = start + length;
        int offset = 0;
        var builder = new StringBuilder();

        foreach (var span in _spans)
        {
            if (span.Control is not null)
            {
                continue;
            }

            var spanText = span.Text;
            int spanStart = offset;
            offset += spanText.Length;

            int from = Math.Max(start, spanStart);
            int to = Math.Min(end, spanStart + spanText.Length);
            if (to <= from)
            {
                continue;
            }

            builder.Append(WrapInTags(span, Html.Escape(spanText.Substring(from - spanStart, to - from))));
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>Wraps already-escaped content in the tags that reproduce a span's formatting.</summary>
    private static string WrapInTags(TextSpan span, string content)
    {
        if (content.Length == 0)
        {
            return content;
        }

        var builder = new StringBuilder(content.Length + 48);

        if (span.Bold)
        {
            builder.Append("<b>");
        }

        if (span.Italic)
        {
            builder.Append("<i>");
        }

        if (span.Underline)
        {
            builder.Append("<u>");
        }

        if (span.Strikethrough)
        {
            builder.Append("<s>");
        }

        var style = StyleOf(span);
        if (style is not null)
        {
            builder.Append("<span style=\"").Append(style).Append("\">");
        }

        builder.Append(content);

        if (style is not null)
        {
            builder.Append("</span>");
        }

        if (span.Strikethrough)
        {
            builder.Append("</s>");
        }

        if (span.Underline)
        {
            builder.Append("</u>");
        }

        if (span.Italic)
        {
            builder.Append("</i>");
        }

        if (span.Bold)
        {
            builder.Append("</b>");
        }

        return builder.ToString();
    }

    /// <summary>The CSS a span's colour and size overrides need, or null when it overrides neither.</summary>
    private static string? StyleOf(TextSpan span)
    {
        StringBuilder? builder = null;

        if (span.ForeColor is { } color)
        {
            builder = new StringBuilder();
            builder.Append("color:#")
                .Append(color.Red.ToString("X2", CultureInfo.InvariantCulture))
                .Append(color.Green.ToString("X2", CultureInfo.InvariantCulture))
                .Append(color.Blue.ToString("X2", CultureInfo.InvariantCulture));
        }

        if (span.FontSize is { } size)
        {
            builder ??= new StringBuilder();
            if (builder.Length > 0)
            {
                builder.Append(';');
            }

            builder.Append("font-size:")
                .Append(size.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("px");
        }

        return builder?.ToString();
    }

    /// <summary>Returns the laid-out text, placing fragments when the content size changed.</summary>
    private Layout? EnsurePlaced()
    {
        var content = ContentRect;
        if (content.Width <= 0f || content.Height <= 0f)
        {
            return null;
        }

        var layout = EnsureLayout(content.Width);
        if (layout is null)
        {
            return null;
        }

        Place(layout, content);
        return layout;
    }

    private static float OffsetInLine(Line line, float x)
    {
        var fragments = line.Fragments;
        if (fragments.Count == 0)
        {
            return 0f;
        }

        for (int i = 0; i < fragments.Count; i++)
        {
            var fragment = fragments[i];
            if (x <= fragment.Bounds.Right || i == fragments.Count - 1)
            {
                return OffsetInFragment(fragment, x);
            }
        }

        return 0f;
    }

    private static float OffsetInFragment(Fragment fragment, float x)
    {
        if (fragment.Kind == FragmentKind.Control || fragment.Text.Length == 0)
        {
            return fragment.Start;
        }

        var text = fragment.Text;
        float left = fragment.Bounds.Left;
        int best = 0;
        float bestDistance = MathF.Abs(x - left);

        for (int i = 1; i <= text.Length; i++)
        {
            float boundary = left + (i == text.Length ? fragment.Width : fragment.Font.MeasureWidth(text[..i]));
            float distance = MathF.Abs(x - boundary);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return fragment.Start + best;
    }

    private static float MeasurePrefix(Fragment fragment, int count)
    {
        if (count <= 0)
        {
            return 0f;
        }

        return count >= fragment.Text.Length ? fragment.Width : fragment.Font.MeasureWidth(fragment.Text[..count]);
    }

    private static bool TryGetLineBounds(Line line, out SKRect bounds)
    {
        bounds = default;
        bool any = false;

        foreach (var fragment in line.Fragments)
        {
            bounds = any ? SKRect.Union(bounds, fragment.Bounds) : fragment.Bounds;
            any = true;
        }

        return any;
    }

    private void SetHoverSpan(TextSpan? span)
    {
        if (ReferenceEquals(span, _hoverSpan))
        {
            return;
        }

        if (_hoverSpan is not null)
        {
            _hoverSpan.IsMouseOver = false;
            _hoverSpan.RaiseMouseLeave();
        }

        _hoverSpan = span;

        if (_hoverSpan is not null)
        {
            _hoverSpan.IsMouseOver = true;
            _hoverSpan.RaiseMouseEnter();
        }

        Invalidate();
    }

    private void ApplyInlineBounds(Layout layout)
    {
        foreach (var line in layout.Lines)
        {
            foreach (var fragment in line.Fragments)
            {
                if (fragment.Kind == FragmentKind.Control && fragment.Span.Control is { } control)
                {
                    control.Bounds = fragment.Bounds;
                }
            }
        }
    }

    /// <summary>Assigns absolute (label-local) bounds to every fragment.</summary>
    private void Place(Layout layout, SKRect content)
    {
        float blockTop = content.Top + (TextAlign switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight
                => (content.Height - layout.Height) / 2f,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight
                => content.Height - layout.Height,
            _ => 0f,
        });

        foreach (var line in layout.Lines)
        {
            float lineTop = blockTop + line.Top;
            float x = content.Left + (TextAlign switch
            {
                ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter
                    => (content.Width - line.Width) / 2f,
                ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight
                    => content.Width - line.Width,
                _ => 0f,
            });

            foreach (var fragment in line.Fragments)
            {
                fragment.Bounds = fragment.Kind == FragmentKind.Control
                    ? new SKRect(x, lineTop + line.Lead + fragment.OffsetY, x + fragment.Width, lineTop + line.Lead + fragment.OffsetY + fragment.Height)
                    : new SKRect(x, lineTop, x + fragment.Width, lineTop + line.Height);
                x += fragment.Width;
            }
        }
    }

    /// <summary>
    /// Fills the hover/press background once per run of adjacent fragments of the same span, so a
    /// highlighted span reads as one continuous shape instead of one rounded box per word and
    /// space.
    /// </summary>
    private void DrawSpanHighlights(SKCanvas canvas, Line line, SKPaint paint)
    {
        var fragments = line.Fragments;
        for (int i = 0; i < fragments.Count; i++)
        {
            var first = fragments[i];
            var span = first.Span;
            if (first.Kind is FragmentKind.Break or FragmentKind.Control ||
                (!span.IsMouseOver && !span.IsPressed) ||
                ResolveHoverBackColor(span) is not { } hoverBack)
            {
                continue;
            }

            i = FindRunEnd(fragments, i, out float left, out float right);

            paint.Color = span.IsPressed ? WithAlpha(hoverBack, 0x33) : hoverBack;
            var rect = new SKRect(left, first.Bounds.Top, right, first.Bounds.Bottom);
            canvas.DrawRoundRect(rect, HighlightRadius, HighlightRadius, paint);
        }
    }

    /// <summary>
    /// Draws underlines and strike-throughs once per run of adjacent fragments of the same span,
    /// so a decoration continues across the spaces between words instead of restarting per word.
    /// </summary>
    private void DrawSpanDecorations(SKCanvas canvas, Line line, SKPaint paint)
    {
        var fragments = line.Fragments;
        for (int i = 0; i < fragments.Count; i++)
        {
            var first = fragments[i];
            var span = first.Span;
            if (first.Kind is FragmentKind.Break or FragmentKind.Control || (!span.Underline && !span.Strikethrough))
            {
                continue;
            }

            i = FindRunEnd(fragments, i, out float left, out float right);

            DrawDecorations(canvas, left, right, first.Bounds.Top + line.Ascent, first.Font, span, ResolveSpanColor(span), paint);
        }
    }

    /// <summary>
    /// The colour a span's text is painted in: its own colour, else the palette entry it names,
    /// else the label's <see cref="Control.ForeColor"/> - except while the label is disabled,
    /// where every span greys out so a link inside a disabled panel stops looking live. Resolved
    /// while painting so that a link keeps up with the theme instead of freezing the accent colour
    /// it was created with.
    /// </summary>
    private SKColor ResolveSpanColor(TextSpan span)
    {
        if (!Enabled)
        {
            return Theme.DisabledForeground;
        }

        if (span.ForeColor is { } literal)
        {
            return literal;
        }

        if (span.ThemeColor is { } role)
        {
            return Theme.ColorOf(role);
        }

        return ForeColor;
    }

    /// <summary>
    /// The fill painted behind a span while it is hovered or pressed: its own colour, else a light
    /// tint of the palette entry it names, so a link's highlight follows the theme.
    /// </summary>
    private static SKColor? ResolveHoverBackColor(TextSpan span)
    {
        if (span.HoverBackColor is { } literal)
        {
            return literal;
        }

        return span.HoverBackColorRole is { } role
            ? Theme.ColorOf(role).WithAlpha(HoverBackAlpha)
            : null;
    }

    /// <summary>
    /// Returns the index of the last fragment in the run of adjacent fragments belonging to the
    /// same span as <paramref name="start"/>, along with the run's horizontal extent. Spaces inside
    /// a run are part of it, so decorations and highlights continue across the gaps between words.
    /// </summary>
    private static int FindRunEnd(List<Fragment> fragments, int start, out float left, out float right)
    {
        var first = fragments[start];
        left = first.Bounds.Left;
        right = first.Bounds.Right;

        int end = start;
        while (end + 1 < fragments.Count)
        {
            var next = fragments[end + 1];
            if (!ReferenceEquals(next.Span, first.Span) ||
                next.Kind is FragmentKind.Break or FragmentKind.Control ||
                next.Bounds.Left > right + 0.5f)
            {
                break;
            }

            right = MathF.Max(right, next.Bounds.Right);
            end++;
        }

        return end;
    }

    private static void DrawDecorations(SKCanvas canvas, float left, float right, float baseline, Font font, TextSpan span, SKColor color, SKPaint paint)
    {
        if (right - left <= 0f)
        {
            return;
        }

        var metrics = font.SkFont.Metrics;
        float size = font.Size;
        float capHeight = metrics.CapHeight > 0f ? metrics.CapHeight : -metrics.Ascent * 0.7f;
        float xHeight = metrics.XHeight > 0f ? metrics.XHeight : capHeight * 0.7f;
        float thickness = MathF.Max(1f, metrics.UnderlineThickness ?? 1f);
        paint.Color = color;

        if (span.Underline)
        {
            // Only the magnitude is meaningful: an underline always sits below the baseline.
            float offset = MathF.Abs(metrics.UnderlinePosition.GetValueOrDefault());
            if (offset < 0.01f || offset > size * 0.35f)
            {
                offset = MathF.Max(1.5f, size * 0.12f);
            }

            float y = baseline + offset;
            canvas.DrawRect(new SKRect(left, y - (thickness / 2f), right, y + (thickness / 2f)), paint);
        }

        if (span.Strikethrough)
        {
            // Only the magnitude is meaningful: a strike-through always sits above the baseline,
            // roughly half an x-height up.
            float offset = MathF.Abs(metrics.StrikeoutPosition.GetValueOrDefault());
            if (offset < size * 0.1f || offset > capHeight)
            {
                offset = xHeight * 0.5f;
            }

            float y = baseline - offset;
            canvas.DrawRect(new SKRect(left, y - (thickness / 2f), right, y + (thickness / 2f)), paint);
        }
    }

    private Layout BuildLayout(float wrapWidth)
    {
        if (wrapWidth <= 0f)
        {
            wrapWidth = float.PositiveInfinity;
        }

        var layout = new Layout { WrapWidth = wrapWidth };
        bool wrap = float.IsFinite(wrapWidth);

        var line = new Line();
        float x = 0f;

        void Commit()
        {
            while (line.Fragments.Count > 0 && line.Fragments[^1].Kind == FragmentKind.Space)
            {
                x -= line.Fragments[^1].Width;
                line.Fragments.RemoveAt(line.Fragments.Count - 1);
            }

            if (line.Fragments.Count == 0)
            {
                x = 0f;
                return;
            }

            line.Width = x;
            layout.Lines.Add(line);
            line = new Line();
            x = 0f;
        }

        foreach (var fragment in BuildFragments(wrapWidth))
        {
            switch (fragment.Kind)
            {
                case FragmentKind.Break:
                {
                    int before = layout.Lines.Count;
                    Commit();
                    if (layout.Lines.Count == before)
                    {
                        // A blank line keeps the paragraph spacing of an empty line.
                        layout.Lines.Add(new Line { Ascent = Font.BaselineOffset, Height = Font.LineHeight });
                    }

                    break;
                }

                case FragmentKind.Control:
                    if (wrap && x + fragment.Width > wrapWidth && line.Fragments.Count > 0)
                    {
                        Commit();
                    }

                    line.Fragments.Add(fragment);
                    x += fragment.Width;
                    break;

                case FragmentKind.Space:
                    if (line.Fragments.Count == 0)
                    {
                        break;
                    }

                    line.Fragments.Add(fragment);
                    x += fragment.Width;
                    break;

                default:
                    if (wrap && x + fragment.Width > wrapWidth)
                    {
                        if (line.Fragments.Count > 0)
                        {
                            Commit();
                        }

                        if (fragment.Width > wrapWidth)
                        {
                            var remaining = fragment.Text;
                            int consumed = 0;
                            while (remaining.Length > 0)
                            {
                                int take = FitPrefix(remaining, fragment.Font, wrapWidth);
                                if (take <= 0)
                                {
                                    take = 1;
                                }

                                var piece = new Fragment
                                {
                                    Span = fragment.Span,
                                    Kind = FragmentKind.Word,
                                    Font = fragment.Font,
                                    Start = fragment.Start + consumed,
                                    Text = remaining[..take],
                                };
                                piece.Width = fragment.Font.MeasureWidth(piece.Text);

                                line.Fragments.Add(piece);
                                x += piece.Width;
                                remaining = remaining[take..];
                                consumed += take;

                                if (remaining.Length > 0)
                                {
                                    Commit();
                                }
                            }

                            break;
                        }
                    }

                    line.Fragments.Add(fragment);
                    x += fragment.Width;
                    break;
            }
        }

        Commit();

        float y = 0f;
        foreach (var current in layout.Lines)
        {
            MeasureLine(current);
            current.Top = y;
            y += current.Height + _lineSpacing;
        }

        layout.Height = layout.Lines.Count > 0 ? y - _lineSpacing : 0f;

        float width = 0f;
        foreach (var current in layout.Lines)
        {
            width = MathF.Max(width, current.Width);
        }

        layout.Width = width;
        return layout;
    }

    private List<Fragment> BuildFragments(float wrapWidth)
    {
        var fragments = new List<Fragment>();
        int offset = 0;

        foreach (var span in _spans)
        {
            if (span.Control is { } control)
            {
                var size = MeasureInlineControl(control, wrapWidth);
                fragments.Add(new Fragment
                {
                    Span = span,
                    Kind = FragmentKind.Control,
                    Start = offset,
                    Width = size.Width,
                    Height = size.Height,
                });
                continue;
            }

            Tokenize(span, ResolveFont(span), fragments, ref offset);
        }

        return fragments;
    }

    /// <summary>
    /// Splits a span's text into word, space and break fragments, assigning each its offset in the
    /// label's <see cref="Text"/> so selection can map between characters and geometry.
    /// </summary>
    private static void Tokenize(TextSpan span, Font font, List<Fragment> output, ref int offset)
    {
        var text = span.Text;
        int index = 0;

        while (index < text.Length)
        {
            char c = text[index];
            if (c == '\r')
            {
                index++;
                offset++;
                continue;
            }

            if (c == '\n')
            {
                output.Add(new Fragment { Span = span, Kind = FragmentKind.Break, Font = font, Start = offset });
                index++;
                offset++;
                continue;
            }

            int start = index;
            if (char.IsWhiteSpace(c))
            {
                while (index < text.Length && text[index] != '\n' && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }

                var run = text[start..index];
                output.Add(new Fragment
                {
                    Span = span,
                    Kind = FragmentKind.Space,
                    Font = font,
                    Start = offset,
                    Text = run,
                    Width = font.MeasureWidth(run),
                });
                offset += run.Length;
                continue;
            }

            while (index < text.Length && !char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            var word = text[start..index];
            output.Add(new Fragment
            {
                Span = span,
                Kind = FragmentKind.Word,
                Font = font,
                Start = offset,
                Text = word,
                Width = font.MeasureWidth(word),
            });
            offset += word.Length;
        }
    }

    private void MeasureLine(Line line)
    {
        float textAscent = 0f;
        float textDescent = 0f;

        foreach (var fragment in line.Fragments)
        {
            if (fragment.Kind is FragmentKind.Word or FragmentKind.Space)
            {
                textAscent = MathF.Max(textAscent, fragment.Font.BaselineOffset);
                textDescent = MathF.Max(textDescent, fragment.Font.LineHeight - fragment.Font.BaselineOffset);
            }
        }

        float textHeight = textAscent + textDescent;
        float top = 0f;
        float bottom = textHeight;

        foreach (var fragment in line.Fragments)
        {
            if (fragment.Kind != FragmentKind.Control)
            {
                continue;
            }

            fragment.OffsetY = fragment.Span.InlineAlignment switch
            {
                InlineAlignment.Top => 0f,
                InlineAlignment.Center => (textHeight - fragment.Height) / 2f,
                _ => textHeight - fragment.Height,
            };

            top = MathF.Min(top, fragment.OffsetY);
            bottom = MathF.Max(bottom, fragment.OffsetY + fragment.Height);
        }

        line.Ascent = textAscent - top;
        line.Lead = -top;
        line.Height = MathF.Max(bottom - top, textHeight);
    }

    private static SKSize MeasureInlineControl(Control control, float wrapWidth)
    {
        var available = new SKSize(
            float.IsFinite(wrapWidth) ? MathF.Max(0f, wrapWidth) : float.PositiveInfinity,
            float.PositiveInfinity);

        var desired = control.Measure(available);
        float width = control.AutoSize || control.Width <= 0f ? desired.Width : control.Width;
        float height = control.AutoSize || control.Height <= 0f ? desired.Height : control.Height;

        return new SKSize(MathF.Max(0f, width), MathF.Max(0f, height));
    }

    /// <summary>Largest prefix of <paramref name="text"/> that fits in <paramref name="maxWidth"/>.</summary>
    private static int FitPrefix(string text, Font font, float maxWidth)
    {
        int low = 0;
        int high = text.Length;

        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (font.MeasureWidth(text[..mid]) <= maxWidth)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }

    private Font ResolveFont(TextSpan span)
    {
        if (span.Font is not null)
        {
            return span.Font;
        }

        int weight = span.Bold ? Math.Max(700, Font.Weight) : Font.Weight;
        bool italic = span.Italic || Font.Italic;
        float size = span.FontSize is { } spanSize ? spanSize : Font.Size;
        string? family = span.FontFamily;

        if (weight == Font.Weight
            && italic == Font.Italic
            && MathF.Abs(size - Font.Size) < 0.01f
            && (family is null || string.Equals(family, Font.FamilyName, StringComparison.Ordinal)))
        {
            return Font;
        }

        string key = string.Concat(family ?? Font.FamilyName, "|", size.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), "|", weight.ToString(), "|", italic ? "1" : "0");
        if (!_derivedFonts.TryGetValue(key, out var font))
        {
            // A span that names a family asks for a different family and is resolved by name. A span
            // that only changes weight, slant or size is derived from the label's own font, so a face
            // loaded from a file stays on that face instead of being looked up on the machine.
            font = family is null
                ? Font.Derive(size, weight, italic)
                : new Font(family, size, weight, italic);
            _derivedFonts[key] = font;
        }

        return font;
    }

    private void ClearDerivedFonts()
    {
        foreach (var font in _derivedFonts.Values)
        {
            font.Dispose();
        }

        _derivedFonts.Clear();
    }

    private static SKColor WithAlpha(SKColor color, byte alpha) => new(color.Red, color.Green, color.Blue, alpha);

    private sealed class Fragment
    {
        public TextSpan Span = null!;
        public FragmentKind Kind;
        public string Text = string.Empty;

        /// <summary>Assigned while the text is tokenized, from the label's effective font.</summary>
        public Font Font = null!;
        public float Width;
        public float Height;
        public float OffsetY;
        public SKRect Bounds;

        /// <summary>Offset of <see cref="Text"/> in the label's <see cref="Text"/>.</summary>
        public int Start;
    }

    private sealed class Line
    {
        public List<Fragment> Fragments { get; } = new();
        public float Width;
        public float Ascent;
        public float Height;
        public float Top;

        /// <summary>How far the line box was pushed down by controls overhanging the text top.</summary>
        public float Lead;
    }

    private sealed class Layout
    {
        public List<Line> Lines { get; } = new();
        public float Width;
        public float Height;
        public float WrapWidth;
    }
}

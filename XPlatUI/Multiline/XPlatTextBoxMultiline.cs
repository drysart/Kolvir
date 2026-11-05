using System.Text;
using SkiaSharp;

namespace XPlatUI.Multiline;

/// <summary>
/// The multi-line editing state of one text box: the visual lines its text is laid out on, the
/// scrollbars that move that text, and the caret's preferred column.
/// </summary>
/// <remarks>
/// <para>
/// One instance exists per text box that turned <c>Multiline</c> on, is created by that property,
/// and is installed into the control as <c>XPlatTextBox.MultilineFeature</c>. Nothing else in the
/// library names this type, so it is trimmed along with the property that creates it.
/// </para>
/// <para>
/// The line model is a list of visual lines. A logical line is the run between two <c>'\n'</c>
/// characters; word wrapping splits a logical line into further visual lines without inserting
/// characters. Every visual line keeps the text index range it covers and the caret and selection
/// remain plain text indices, so the control's public selection API keeps working unchanged.
/// </para>
/// <para>
/// Scrolling is delegated to <see cref="XPlatScrollBars"/>, the helper <see cref="XPlatPanel"/>
/// uses, so the bars, the wheel and the clamping behave like every other scrolling control. The
/// text itself is painted in client coordinates shifted by the helper's
/// <see cref="XPlatScrollBars.ChildOffset"/>.
/// </para>
/// <para>
/// The visual line the caret is on is <em>derived</em> from <see cref="XPlatTextBox.CaretIndex"/>
/// rather than stored, so every path that moves the caret - a click, an arrow key, an edit, the
/// autocomplete completing a value - keeps the layout consistent without knowing about it. Only
/// the preferred column of an Up/Down run is remembered, and it is remembered next to the index it
/// was taken at, so any other move invalidates it without a hook.
/// </para>
/// </remarks>
internal sealed class XPlatTextBoxMultiline : IXPlatTextBoxMultiline
{
    /// <summary>How wide a wrapped box asks to be when nothing constrains it.</summary>
    private const float PreferredWidth = 320f;

    /// <summary>How many lines a box asks to be tall for when nothing constrains it.</summary>
    private const int PreferredLines = 6;

    /// <summary>Space a caret keeps from the edge of the text area, so it never sits on the border.</summary>
    private const float CaretMargin = 2f;

    private readonly XPlatTextBox _textBox;
    private readonly XPlatScrollBars _scroll;

    private List<TextLine>? _lines;
    private string? _linesText;
    private XPlatFont? _linesFont;
    private char? _linesMask;
    private float _linesWidth = float.NaN;
    private bool _linesWrap;
    private bool _wordWrap = true;
    private float? _preferredX;
    private int _preferredIndex = -1;
    private int _caretLine = -1;
    private int _caretLineIndex = -1;
    private bool _layingOut;

    private XPlatTextBoxMultiline(XPlatTextBox textBox)
    {
        _textBox = textBox;
        _scroll = new XPlatScrollBars(textBox);
    }

    /// <summary>Turns multi-line editing on for <paramref name="textBox"/>, which must not have it yet.</summary>
    public static void Attach(XPlatTextBox textBox)
    {
        var feature = new XPlatTextBoxMultiline(textBox);
        textBox.MultilineFeature = feature;

        // From here on the control's own Text setter normalises what it is given too, so the state
        // the box starts from is already the one the line model understands.
        textBox.Text = feature.SanitizeText(textBox.Text);

        feature._scroll.Vertical = XPlatScrollStyle.Automatic;
        feature._scroll.Horizontal = feature._wordWrap ? XPlatScrollStyle.None : XPlatScrollStyle.Automatic;
        textBox.PerformLayout();
        textBox.Invalidate();
    }

    /// <summary>Releases the feature and collapses the text back to the one line the control can draw.</summary>
    public static void Detach(XPlatTextBox textBox)
    {
        if (textBox.MultilineFeature is not { } feature)
        {
            return;
        }

        // The seam goes first, so the assignment below goes through the single-line setter and the
        // feature is not asked to lay out text it is no longer laying out.
        textBox.MultilineFeature = null;
        feature.Dispose();

        textBox.Text = XPlatTextBox.SanitizeSingleLine(textBox.Text);
        textBox.PerformLayout();
        textBox.Invalidate();
    }

    /// <inheritdoc />
    public bool WordWrap
    {
        get => _wordWrap;
        set
        {
            if (_wordWrap == value)
            {
                return;
            }

            _wordWrap = value;

            // Turning wrapping on takes the horizontal offset away with it: there is nothing left to
            // scroll sideways, and leaving the text shifted would hide its left edge.
            _scroll.Horizontal = value ? XPlatScrollStyle.None : XPlatScrollStyle.Automatic;
            if (value)
            {
                _scroll.ScrollTo(0f, _scroll.VerticalOffset);
            }

            _textBox.Invalidate();
        }
    }

    /// <inheritdoc />
    public XPlatScrollBar? VerticalScrollBar => _scroll.VerticalBar;

    /// <inheritdoc />
    public XPlatScrollBar? HorizontalScrollBar => _scroll.HorizontalBar;

    /// <inheritdoc />
    public float VerticalOffset => _scroll.VerticalOffset;

    /// <inheritdoc />
    public float HorizontalOffset => _scroll.HorizontalOffset;

    /// <inheritdoc />
    public string SanitizeText(string text)
    {
        var normalized = NormalizeLineBreaks(text);
        if (normalized.Length == 0)
        {
            return normalized;
        }

        var builder = new StringBuilder(normalized.Length);
        foreach (char c in normalized)
        {
            if (c == '\n')
            {
                builder.Append('\n');
            }
            else if (c == '\t')
            {
                builder.Append("    ");
            }
            else if (!char.IsControl(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Normalises line breaks to the single line feed the line model understands: CRLF is one break,
    /// a lone CR is one break too.
    /// </summary>
    private static string NormalizeLineBreaks(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('\r') < 0)
        {
            return text ?? string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\r')
            {
                builder.Append(c);
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public SKSize Measure(XPlatTextBox textBox, XPlatTextBoxViewport viewport, SKSize availableSize)
    {
        float width = availableSize.Width > 0f ? MathF.Min(availableSize.Width, PreferredWidth) : PreferredWidth;
        var lines = BuildLines(width, _wordWrap);

        float widest = 0f;
        foreach (var line in lines)
        {
            widest = MathF.Max(widest, line.Width);
        }

        // A wrapped box is exactly as wide as it was allowed to be, because that is what its lines
        // were wrapped to; an unwrapped one is as wide as its longest line, up to the same cap.
        float contentWidth = _wordWrap ? width : MathF.Min(MathF.Ceiling(widest) + 4f, PreferredWidth);
        float contentHeight = (MathF.Min(lines.Count, PreferredLines) * textBox.Font.LineHeight) + 2f;

        return new SKSize(MathF.Max(1f, contentWidth), MathF.Max(1f, contentHeight));
    }

    /// <inheritdoc />
    public void Layout(XPlatTextBox textBox, XPlatTextBoxViewport viewport)
    {
        if (_layingOut)
        {
            return;
        }

        _layingOut = true;
        try
        {
            _scroll.Layout(viewport.Client, viewport.Padding, area => Arrange(area));
        }
        finally
        {
            _layingOut = false;
        }
    }

    /// <inheritdoc />
    public void Paint(XPlatTextBox textBox, SKCanvas canvas, XPlatTextBoxViewport viewport, string placeholderText)
    {
        var content = ContentOf(viewport);
        if (content.Width <= 0f || content.Height <= 0f)
        {
            return;
        }

        var lines = LinesOf(viewport);
        var font = textBox.Font;
        var display = DisplayText();
        var offset = _scroll.ChildOffset;
        float lineHeight = font.LineHeight;

        canvas.Save();
        canvas.ClipRect(content);

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        // The selection and the annotation squiggles are painted by the control before this call,
        // and the caret after it, so all three land in the same order on a one-line field too.

        if (textBox.Text.Length == 0 && !string.IsNullOrEmpty(placeholderText))
        {
            paint.Color = XPlatTheme.Placeholder;
            canvas.DrawText(
                placeholderText,
                content.Left + offset.X,
                content.Top + offset.Y + font.BaselineOffset,
                SKTextAlign.Left,
                font.SkFont,
                paint);
        }
        else
        {
            paint.Color = textBox.Enabled ? textBox.ForeColor : XPlatTheme.DisabledForeground;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.End <= line.Start)
                {
                    continue;
                }

                float top = content.Top + offset.Y + (i * lineHeight);
                if (top + lineHeight <= content.Top || top >= content.Bottom)
                {
                    continue;
                }

                canvas.DrawText(
                    display[line.Start..line.End],
                    content.Left + offset.X,
                    top + font.BaselineOffset,
                    SKTextAlign.Left,
                    font.SkFont,
                    paint);
            }
        }

        // The caret is not drawn here: the control draws it after the annotation squiggles, so the
        // order is the same on a one-line and a multi-line field.

        canvas.Restore();
    }

    /// <inheritdoc />
    public SKRect GetCaretBounds(XPlatTextBox textBox, XPlatTextBoxViewport viewport) =>
        CaretRect(textBox, viewport, scrolled: true);

    /// <summary>
    /// Where the caret is drawn. <paramref name="scrolled"/> chooses the coordinate space: true is
    /// the control's client coordinates, which is what painting and the autocomplete popup want;
    /// false is the space the text is laid out in, where a caret on line <c>n</c> is at
    /// <c>n * LineHeight</c> however far the view has been scrolled. The scroll helper works in the
    /// second space (it subtracts the offset itself), so handing it the first would count the offset
    /// twice and leave a caret that has walked to the end of a long document on a line still below
    /// the viewport.
    /// </summary>
    private SKRect CaretRect(XPlatTextBox textBox, XPlatTextBoxViewport viewport, bool scrolled)
    {
        var content = ContentOf(viewport);
        if (content.Width <= 0f || content.Height <= 0f)
        {
            return SKRect.Empty;
        }

        var lines = LinesOf(viewport);
        var font = textBox.Font;
        var offset = scrolled ? _scroll.ChildOffset : SKPoint.Empty;

        int caret = CaretOf(textBox);
        int lineIndex = CaretLineOf(lines, caret);
        var line = lines[lineIndex];
        int at = Math.Clamp(caret, line.Start, line.End);

        float x = content.Left + offset.X + MeasureWidth(font, DisplayText(), line.Start, at);
        float y = content.Top + offset.Y + (lineIndex * font.LineHeight);
        return new SKRect(x, y, x + textBox.Caret.Width, y + font.LineHeight);
    }

    /// <inheritdoc />
    public int GetIndexFromPoint(XPlatTextBox textBox, XPlatTextBoxViewport viewport, SKPoint point)
    {
        var content = ContentOf(viewport);
        if (content.Width <= 0f || content.Height <= 0f)
        {
            return textBox.CaretIndex;
        }

        var lines = LinesOf(viewport);
        var offset = _scroll.ChildOffset;
        float lineHeight = MathF.Max(1f, textBox.Font.LineHeight);

        float y = point.Y - content.Top - offset.Y;
        int lineIndex = Math.Clamp((int)MathF.Floor(y / lineHeight), 0, lines.Count - 1);

        int index = IndexFromXOnLine(lines, lineIndex, point.X - content.Left - offset.X);

        // The caret is about to go where the pointer is, so that is the line it is on even when the
        // point is exactly at a soft wrap.
        PinCaretLine(lineIndex, index);

        return index;
    }

    /// <inheritdoc />
    public void GetSelectionRects(
        XPlatTextBox textBox,
        XPlatTextBoxViewport viewport,
        int start,
        int length,
        List<SKRect> output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (length <= 0)
        {
            return;
        }

        var content = ContentOf(viewport);
        if (content.Width <= 0f || content.Height <= 0f)
        {
            return;
        }

        var lines = LinesOf(viewport);
        var display = DisplayText();
        var font = textBox.Font;
        var offset = _scroll.ChildOffset;

        int selectionStart = Math.Clamp(start, 0, textBox.Text.Length);
        int selectionEnd = Math.Clamp(start + length, 0, textBox.Text.Length);
        if (selectionEnd <= selectionStart)
        {
            return;
        }

        float lineHeight = font.LineHeight;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (selectionEnd <= line.Start || selectionStart > line.End)
            {
                continue;
            }

            int from = Math.Max(selectionStart, line.Start);
            int to = Math.Min(selectionEnd, line.End);

            float left = MeasureWidth(font, display, line.Start, from);
            float right = MeasureWidth(font, display, line.Start, to);

            // A selected line break is drawn as a small block past the end of its line, so the
            // break is visibly part of the selection. A soft wrap has no character there, so it
            // never gets one.
            if (line.HasNewline && selectionStart <= line.End && selectionEnd > line.End)
            {
                right = MathF.Max(right, line.Width) + textBox.Caret.Width;
            }

            float x1 = content.Left + offset.X + left;
            float x2 = content.Left + offset.X + right;
            float y1 = content.Top + offset.Y + (i * lineHeight);
            float y2 = y1 + lineHeight;

            x1 = MathF.Max(x1, content.Left);
            x2 = MathF.Min(x2, content.Right);
            y1 = MathF.Max(y1, content.Top);
            y2 = MathF.Min(y2, content.Bottom);

            if (x2 > x1 && y2 > y1)
            {
                output.Add(new SKRect(x1, y1, x2, y2));
            }
        }
    }

    /// <summary>
    /// The keys that differ once text spans lines. Everything else - Left, Right, Backspace,
    /// Delete and the Ctrl commands - works on plain text indices and is left to the control,
    /// which is why joining a line with Backspace needs no code here either.
    /// </summary>
    public bool OnKeyDown(XPlatTextBox textBox, XPlatTextBoxViewport viewport, XPlatKeyEventArgs e)
    {
        bool shift = (e.Modifiers & XPlatModifiers.Shift) != 0;
        bool control = (e.Modifiers & XPlatModifiers.Control) != 0;

        switch (e.Key)
        {
            case XPlatKey.Enter when !control:
                // Ctrl+Enter is deliberately left alone: it is the gesture a form uses to accept,
                // and a field that spans lines still needs one.
                e.Handled = true;
                if (!textBox.ReadOnly)
                {
                    textBox.InsertText("\n");
                }

                return true;

            case XPlatKey.Up:
                MoveByLines(textBox, viewport, -1, shift);
                e.Handled = true;
                return true;

            case XPlatKey.Down:
                MoveByLines(textBox, viewport, 1, shift);
                e.Handled = true;
                return true;

            case XPlatKey.PageUp:
                MoveByPage(textBox, viewport, -1, shift);
                e.Handled = true;
                return true;

            case XPlatKey.PageDown:
                MoveByPage(textBox, viewport, 1, shift);
                e.Handled = true;
                return true;

            case XPlatKey.Home when !control:
            {
                var lines = LinesOf(viewport);
                int line = CaretLineOf(lines, CaretOf(textBox));
                MoveTo(textBox, lines[line].Start, shift);
                PinCaretLine(line, textBox.CaretIndex);
                e.Handled = true;
                return true;
            }

            case XPlatKey.End when !control:
            {
                var lines = LinesOf(viewport);
                int line = CaretLineOf(lines, CaretOf(textBox));
                MoveTo(textBox, lines[line].End, shift);
                PinCaretLine(line, textBox.CaretIndex);
                e.Handled = true;
                return true;
            }

            default:
                return false;
        }
    }

    /// <inheritdoc />
    public bool OnMouseWheel(XPlatTextBox textBox, XPlatMouseEventArgs e)
    {
        if (_scroll.ContentArea.Width <= 0f || _scroll.ContentArea.Height <= 0f)
        {
            return false;
        }

        bool shift = (e.Modifiers & XPlatModifiers.Shift) != 0;
        bool vertical = _scroll.MaxVerticalOffset > XPlatScrollBars.ScrollEpsilon;
        bool horizontal = _scroll.MaxHorizontalOffset > XPlatScrollBars.ScrollEpsilon;

        if (vertical && !(shift && horizontal))
        {
            float before = _scroll.VerticalOffset;
            _scroll.ScrollBy(0f, -e.Delta);
            return MathF.Abs(_scroll.VerticalOffset - before) > XPlatScrollBars.ScrollEpsilon;
        }

        if (horizontal)
        {
            float before = _scroll.HorizontalOffset;
            _scroll.ScrollBy(-e.Delta, 0f);
            return MathF.Abs(_scroll.HorizontalOffset - before) > XPlatScrollBars.ScrollEpsilon;
        }

        return false;
    }

    /// <inheritdoc />
    public void OnTextChanged(XPlatTextBox textBox, XPlatTextBoxViewport viewport)
    {
        // The line cache is keyed on the text it was built from, so a new value rebuilds it on the
        // next question rather than needing a hook here. What is left is the preferred column: the
        // caret moved because the text changed, so a later Up/Down must not reuse it.
        _preferredX = null;
        _preferredIndex = -1;

        // The box may have shrunk or grown; a scroll offset that is now past the end has to go.
        _scroll.ScrollTo(_scroll.HorizontalOffset, _scroll.VerticalOffset);
    }

    /// <inheritdoc />
    public void EnsureCaretVisible(XPlatTextBox textBox, XPlatTextBoxViewport viewport)
    {
        if (_layingOut || viewport.Client.Width <= 0f || viewport.Client.Height <= 0f)
        {
            return;
        }

        if (_scroll.ContentArea.Width <= 0f || _scroll.ContentArea.Height <= 0f)
        {
            // Nothing has been arranged yet, which is the state a caret query arrives in before the
            // first layout. Laying out now gives the scroll helper the viewport it needs.
            Layout(textBox, viewport);
            if (_scroll.ContentArea.Width <= 0f || _scroll.ContentArea.Height <= 0f)
            {
                return;
            }
        }

        var caret = CaretRect(textBox, viewport, scrolled: false);
        if (caret.IsEmpty)
        {
            return;
        }

        _scroll.ScrollRectIntoView(XPlatGeometry.Inflate(caret, CaretMargin));
    }

    /// <inheritdoc />
    public void RenderBars(SKCanvas canvas) => _scroll.RenderBars(canvas);

    /// <inheritdoc />
    public XPlatControl? HitTestBar(SKPoint pointInLocal) =>
        _scroll.VerticalBar?.HitTest(pointInLocal) ?? _scroll.HorizontalBar?.HitTest(pointInLocal);

    /// <inheritdoc />
    public void ClampScroll(XPlatTextBox textBox, XPlatTextBoxViewport viewport) =>
        _scroll.ScrollTo(_scroll.HorizontalOffset, _scroll.VerticalOffset);

    /// <inheritdoc />
    public void Dispose()
    {
        _lines = null;
        _linesText = null;
        _linesFont = null;
        _scroll.Dispose();
    }

    /// <summary>
    /// Positions the lines and returns the size of the text content. The scrollbar helper calls
    /// this once per visibility pass, so it must be idempotent.
    /// </summary>
    private SKSize Arrange(SKRect area)
    {
        if (area.Width <= 0f || area.Height <= 0f)
        {
            return SKSize.Empty;
        }

        var lines = BuildLines(area.Width, _wordWrap);
        CacheLines(lines, area.Width, _wordWrap);

        float widest = 0f;
        foreach (var line in lines)
        {
            widest = MathF.Max(widest, line.Width);
        }

        return new SKSize(_wordWrap ? area.Width : widest, lines.Count * _textBox.Font.LineHeight);
    }

    /// <summary>The area the text is laid out, clipped and hit tested inside.</summary>
    private SKRect ContentOf(XPlatTextBoxViewport viewport)
    {
        var area = _scroll.ContentArea;
        return area.Width > 0f && area.Height > 0f ? area : viewport.Text;
    }

    private List<TextLine> LinesOf(XPlatTextBoxViewport viewport)
    {
        float width = ContentOf(viewport).Width;
        if (width <= 0f)
        {
            // A caret query can arrive before the box has a size. One visual line per logical line
            // is the right answer for a box that cannot wrap, and a safe one for a box that can.
            width = float.PositiveInfinity;
        }

        var font = _textBox.Font;
        char? mask = MaskOf();
        bool current =
            _lines is not null &&
            ReferenceEquals(_linesText, _textBox.Text) &&
            ReferenceEquals(_linesFont, font) &&
            _linesMask == mask &&
            _linesWrap == _wordWrap &&
            MathF.Abs(_linesWidth - width) < 0.01f;

        if (!current)
        {
            CacheLines(BuildLines(width, _wordWrap), width, _wordWrap);
        }

        return _lines!;
    }

    private void CacheLines(List<TextLine> lines, float width, bool wrap)
    {
        _lines = lines;
        _linesText = _textBox.Text;
        _linesFont = _textBox.Font;
        _linesMask = MaskOf();
        _linesWidth = width;
        _linesWrap = wrap;
    }

    /// <summary>The mask character the text is drawn with, or null when it is drawn as it is.</summary>
    private char? MaskOf() => _textBox.IsPassword ? _textBox.PasswordChar : null;

    /// <summary>
    /// The text as it is drawn: password masking replaces every character but the line breaks, so a
    /// masked field still shows how many lines it has. Indices are the same as
    /// <see cref="XPlatControl.Text"/> throughout, which is what lets the caret and the selection go
    /// on working on plain text indices.
    /// </summary>
    private string DisplayText()
    {
        var text = _textBox.Text;
        if (!_textBox.IsPassword || text.Length == 0)
        {
            return text;
        }

        char mask = _textBox.PasswordChar;
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] != '\n')
            {
                chars[i] = mask;
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// Splits the text into visual lines. Each logical line is wrapped on its own, so a line break
    /// is always a line end and a wrap never moves text across one.
    /// </summary>
    private List<TextLine> BuildLines(float wrapWidth, bool wrap)
    {
        var display = DisplayText();
        var lines = new List<TextLine>();

        int start = 0;
        while (true)
        {
            int newline = display.IndexOf('\n', start);
            int end = newline < 0 ? display.Length : newline;

            AddLines(lines, display, start, end, newline >= 0, wrapWidth, wrap);

            if (newline < 0)
            {
                return lines;
            }

            start = newline + 1;
        }
    }

    /// <summary>
    /// Adds the visual lines one logical line occupies. The wrap point is the start of a word that
    /// does not fit, so the whitespace before it stays at the end of the previous line where it is
    /// invisible rather than opening the next one.
    /// </summary>
    private void AddLines(
        List<TextLine> lines,
        string display,
        int start,
        int end,
        bool hasNewline,
        float wrapWidth,
        bool wrap)
    {
        if (end <= start)
        {
            lines.Add(new TextLine(start, end, hasNewline, 0f));
            return;
        }

        if (!wrap || float.IsPositiveInfinity(wrapWidth) || wrapWidth <= 0f)
        {
            lines.Add(new TextLine(start, end, hasNewline, MeasureWidth(_textBox.Font, display, start, end)));
            return;
        }

        int lineStart = start;
        int cursor = start;
        float used = 0f;

        while (cursor < end)
        {
            int unitStart = cursor;
            bool whitespace = IsWhitespace(display[unitStart]);
            while (cursor < end && IsWhitespace(display[cursor]) == whitespace)
            {
                cursor++;
            }

            int unitEnd = cursor;
            float unitWidth = MeasureWidth(_textBox.Font, display, unitStart, unitEnd);

            if (!whitespace && used > 0f && used + unitWidth > wrapWidth)
            {
                lines.Add(new TextLine(lineStart, unitStart, false, MeasureWidth(_textBox.Font, display, lineStart, unitStart)));
                lineStart = unitStart;
                used = 0f;
            }

            if (!whitespace && used <= 0f && unitWidth > wrapWidth)
            {
                // One unit wider than the box - a long word, a URL - can only be broken between two
                // of its own characters. At least one character per line, or the loop never ends.
                int fit = unitStart;
                float run = 0f;
                for (int i = unitStart; i < unitEnd; i++)
                {
                    float character = MeasureWidth(_textBox.Font, display, i, i + 1);
                    if (i > unitStart && run + character > wrapWidth)
                    {
                        break;
                    }

                    run += character;
                    fit = i + 1;
                }

                lines.Add(new TextLine(lineStart, fit, false, run));
                lineStart = fit;
                used = 0f;
                cursor = fit;
                continue;
            }

            used += unitWidth;
        }

        lines.Add(new TextLine(lineStart, end, hasNewline, MeasureWidth(_textBox.Font, display, lineStart, end)));
    }

    private static bool IsWhitespace(char c) => c == ' ' || c == '\t';

    private static float MeasureWidth(XPlatFont font, string display, int start, int end)
    {
        if (end <= start)
        {
            return 0f;
        }

        start = Math.Clamp(start, 0, display.Length);
        end = Math.Clamp(end, start, display.Length);
        return font.MeasureWidth(display[start..end]);
    }

    /// <summary>The visual line an index is drawn on: the first one whose range reaches it.</summary>
    private static int LineOfIndex(List<TextLine> lines, int index)
    {
        int low = 0;
        int high = lines.Count - 1;

        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (index <= lines[middle].End)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low;
    }

    private int IndexFromXOnLine(List<TextLine> lines, int lineIndex, float x)
    {
        var display = DisplayText();
        var font = _textBox.Font;
        var line = lines[Math.Clamp(lineIndex, 0, lines.Count - 1)];

        if (x <= 0f)
        {
            return line.Start;
        }

        if (x >= line.Width)
        {
            return line.End;
        }

        int best = line.Start;
        float bestDistance = float.MaxValue;
        float width = 0f;

        for (int i = line.Start; i <= line.End; i++)
        {
            float distance = MathF.Abs(width - x);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }

            if (i < line.End)
            {
                width += MeasureWidth(font, display, i, i + 1);
            }
        }

        return best;
    }

    /// <summary>The caret index, clamped, so a line lookup is always indexable.</summary>
    private static int CaretOf(XPlatTextBox textBox) => Math.Clamp(textBox.CaretIndex, 0, textBox.Text.Length);

    /// <summary>
    /// The visual line the caret is drawn on. A soft wrap has no character at the point where one
    /// line ends and the next begins, so an index there describes two visual positions. The one the
    /// caret was last <em>put</em> on wins for as long as the caret is still in that line's range,
    /// which is what keeps Home, End, Up and Down from skipping the line the user is looking at.
    /// Everywhere else the caret belongs to the earlier of the two, which is where a caret that
    /// arrived by typing at the wrap belongs.
    /// </summary>
    private int CaretLineOf(List<TextLine> lines, int caret)
    {
        if (_caretLine >= 0 &&
            _caretLine < lines.Count &&
            _caretLineIndex == caret &&
            caret >= lines[_caretLine].Start &&
            caret <= lines[_caretLine].End)
        {
            return _caretLine;
        }

        return LineOfIndex(lines, caret);
    }

    private void PinCaretLine(int line, int caret)
    {
        _caretLine = line;
        _caretLineIndex = caret;
    }

    /// <summary>
    /// Moves the caret to another visual line, keeping the column it wants. <paramref name="x"/> is
    /// the column the caret asked for, in pixels from the start of its line; it is reused for as
    /// long as the caret stays where the previous move put it, so Up and Down walk a short line out
    /// and come back out to the column they started in.
    /// </summary>
    private void MoveByLines(XPlatTextBox textBox, XPlatTextBoxViewport viewport, int delta, bool extend)
    {
        var lines = LinesOf(viewport);
        var font = textBox.Font;
        var display = DisplayText();

        int caret = CaretOf(textBox);
        int lineIndex = CaretLineOf(lines, caret);
        var line = lines[lineIndex];

        float x = _preferredIndex == caret && _preferredX is { } preferred
            ? preferred
            : MeasureWidth(font, display, line.Start, Math.Clamp(caret, line.Start, line.End));

        int target = Math.Clamp(lineIndex + delta, 0, lines.Count - 1);
        int index = IndexFromXOnLine(lines, target, x);

        MoveTo(textBox, index, extend);
        PinCaretLine(target, textBox.CaretIndex);

        _preferredX = x;
        _preferredIndex = textBox.CaretIndex;
    }

    /// <summary>
    /// Turns a page: the caret moves a viewport's worth of lines and the view moves with it, keeping
    /// the caret's row. One line is left of overlap, so the eye can follow the text across the turn.
    /// </summary>
    private void MoveByPage(XPlatTextBox textBox, XPlatTextBoxViewport viewport, int direction, bool extend)
    {
        var content = ContentOf(viewport);
        float lineHeight = MathF.Max(1f, textBox.Font.LineHeight);

        int page = Math.Max(1, (int)(content.Height / lineHeight) - 1);
        MoveByLines(textBox, viewport, direction * page, extend);
        _scroll.ScrollBy(0f, direction * page * lineHeight);
    }

    /// <summary>Moves the caret, extending the selection from its anchor when Shift is held.</summary>
    private static void MoveTo(XPlatTextBox textBox, int index, bool extend)
    {
        index = Math.Clamp(index, 0, textBox.Text.Length);

        if (!extend)
        {
            textBox.CaretIndex = index;
            return;
        }

        // Select() always leaves the caret at the end of the range, so a backwards extension has to
        // name the anchor and the caret separately. The anchor is whichever end of the selection the
        // caret is not on.
        int anchor = textBox.SelectionLength == 0
            ? textBox.CaretIndex
            : textBox.CaretIndex == textBox.SelectionStart
                ? textBox.SelectionStart + textBox.SelectionLength
                : textBox.SelectionStart;

        textBox.SetSelection(anchor, index);
    }

    /// <summary>One visual line: a text range, whether the character after it is a line break, and its width.</summary>
    private readonly struct TextLine
    {
        public TextLine(int start, int end, bool hasNewline, float width)
        {
            Start = start;
            End = end;
            HasNewline = hasNewline;
            Width = width;
        }

        public int Start { get; }

        public int End { get; }

        public bool HasNewline { get; }

        public float Width { get; }
    }
}

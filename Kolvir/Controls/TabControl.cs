using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A container that shows one <see cref="TabPage"/> at a time, with a strip of tabs along its
/// top edge to choose between them.
/// </summary>
/// <remarks>
/// Pages are ordinary children, so they are added through <see cref="TabPages"/>, which is the same
/// collection as <see cref="Control.Controls"/>. Only the selected page is visible, which is
/// what keeps a tab control with many pages cheap: the pages that are not showing are neither
/// painted nor hit tested. The strip scrolls when the captions do not fit.
/// </remarks>
public class TabControl : Control
{
    private const float TabGap = 2f;
    private const float TabPadding = 12f;
    private const float MinTabWidth = 56f;
    private const float MaxTabWidth = 220f;
    private const float StripHeight = 34f;
    private const float IndicatorHeight = 2f;

    private readonly List<SKRect> _tabRects = [];
    private readonly List<IDisposable> _captionTokens = [];
    private readonly List<string> _captions = [];
    private Action<object>? _selectedIndexChanged;
    private int _selectedIndex = -1;
    private int _hotIndex = -1;
    private float _stripOffset;
    private bool _tabsDirty = true;

    public TabControl()
    {
        Focusable = true;
        TabStop = true;
        BorderWidth = 1f;
        CornerRadius = 8f;
        Padding = new Thickness(0f);
        MinimumSize = new SKSize(160f, 90f);
        Size = new SKSize(360f, 220f);
    }

    /// <summary>A tab control is a raised surface until <see cref="Control.Background"/> is assigned.</summary>
    protected override Background DefaultBackground => Background.FromThemeColor(ThemeColor.Surface);

    /// <summary>The pages of the control, in tab order.</summary>
    public ControlCollection TabPages => Controls;

    /// <summary>The captions are measured with the effective font, so a font change re-measures them.</summary>
    protected override void OnAppearanceChanged() => _tabsDirty = true;

    /// <summary>Index of the selected page, or -1 when there are no pages.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int index = Controls.Count == 0 ? -1 : Math.Clamp(value, 0, Controls.Count - 1);

            if (_selectedIndex == index)
            {
                return;
            }

            _selectedIndex = index;
            ApplySelection();
            _tabsDirty = true;
            Invalidate();
            _selectedIndexChanged?.Invoke(this);
        }
    }

    /// <summary>The selected page, or null when there are no pages.</summary>
    public TabPage? SelectedTab =>
        _selectedIndex >= 0 && _selectedIndex < Controls.Count ? Controls[_selectedIndex] as TabPage : null;

    /// <summary>Raised when <see cref="SelectedIndex"/> changed.</summary>
    public IDisposable AddSelectedIndexChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _selectedIndexChanged += handler;
        return new HandlerToken<Action<object>>(RemoveSelectedIndexChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddSelectedIndexChangedHandler"/> added.</summary>
    public void RemoveSelectedIndexChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _selectedIndexChanged -= handler;
    }

    protected override SKSize MeasureContent(SKSize availableSize) => new(360f, 220f);

    protected override void OnLayout()
    {
        base.OnLayout();

        if (_selectedIndex >= Controls.Count)
        {
            _selectedIndex = Controls.Count - 1;
        }

        if (_selectedIndex < 0 && Controls.Count > 0)
        {
            _selectedIndex = 0;
        }

        _tabsDirty = true;
        ApplySelection();

        var content = ContentArea;

        for (int i = 0; i < Controls.Count; i++)
        {
            Controls[i].Bounds = content;
        }
    }

    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        Painter.Fill(canvas, rect, Enabled ? BackColor ?? Theme.Surface : Theme.DisabledBackground, CornerRadius);
        Painter.DrawBorder(canvas, rect, BorderColor, BorderWidth > 0f ? BorderWidth : 1f, CornerRadius);
    }

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        EnsureTabs();

        var content = ContentArea;

        // The rule under the strip runs the full width, so the tabs read as sitting on the content
        // surface rather than floating above it.
        Painter.Fill(
            canvas,
            new SKRect(rect.Left + BorderWidth, content.Top - IndicatorHeight, rect.Right - BorderWidth, content.Top),
            Theme.Border);

        canvas.Save();
        canvas.ClipRect(new SKRect(rect.Left, rect.Top, rect.Right, content.Top));

        for (int i = 0; i < _tabRects.Count; i++)
        {
            PaintTab(canvas, i, _tabRects[i]);
        }

        canvas.Restore();

        if (IsFocused && _selectedIndex >= 0 && _selectedIndex < _tabRects.Count)
        {
            Painter.DrawFocusRing(canvas, _tabRects[_selectedIndex], Theme.FocusRing, 1f, 4f);
        }
    }

    protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        // The frame is drawn in OnPaintBackground so it stays behind the tabs.
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left || !Enabled)
        {
            return;
        }

        Focus();

        int index = TabIndexFromPoint(e.Location);
        if (index >= 0)
        {
            SelectedIndex = index;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        int index = TabIndexFromPoint(e.Location);
        if (index == _hotIndex)
        {
            return;
        }

        _hotIndex = index;
        Invalidate();
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();

        if (_hotIndex == -1)
        {
            return;
        }

        _hotIndex = -1;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e.Handled)
        {
            return;
        }

        EnsureTabs();

        if (e.Location.Y > ContentArea.Top || _tabRects.Count == 0)
        {
            return;
        }

        float total = _tabRects[^1].Right + _stripOffset;
        float overflow = total - StripViewport.Width;

        if (overflow <= 0f)
        {
            return;
        }

        float next = Math.Clamp(_stripOffset - MathF.Sign(e.Delta) * StripHeight, 0f, overflow);

        if (MathF.Abs(next - _stripOffset) < 0.01f)
        {
            return;
        }

        _stripOffset = next;
        _tabsDirty = true;
        Invalidate();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!Enabled || e.Handled)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                SelectedIndex = _selectedIndex - 1;
                e.Handled = true;
                break;

            case Key.Right:
                SelectedIndex = _selectedIndex + 1;
                e.Handled = true;
                break;

            case Key.Home:
                SelectedIndex = 0;
                e.Handled = true;
                break;

            case Key.End:
                SelectedIndex = Controls.Count - 1;
                e.Handled = true;
                break;

            case Key.Tab when (e.Modifiers & Modifiers.Shift) != 0:
                SelectedIndex = _selectedIndex - 1;
                e.Handled = true;
                break;

            case Key.Tab:
                SelectedIndex = _selectedIndex + 1;
                e.Handled = true;
                break;
        }
    }

    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        Invalidate();
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        Invalidate();
    }

    protected override void OnEnabledChanged()
    {
        base.OnEnabledChanged();
        Invalidate();
    }

    internal override void OnControlAdded(Control control)
    {
        base.OnControlAdded(control);

        _captionTokens.Add(control.AddTextChangedHandler(OnPageCaptionChanged));
        _tabsDirty = true;

        if (_selectedIndex < 0)
        {
            _selectedIndex = 0;
        }

        ApplySelection();
        Invalidate();
    }

    internal override void OnControlRemoved(Control control)
    {
        base.OnControlRemoved(control);

        if (_selectedIndex >= Controls.Count)
        {
            _selectedIndex = Controls.Count - 1;
        }

        _tabsDirty = true;
        ApplySelection();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var token in _captionTokens)
            {
                token.Dispose();
            }

            _captionTokens.Clear();
        }

        base.Dispose(disposing);
    }

    private SKRect ContentArea
    {
        get
        {
            float inset = BorderWidth > 0f ? BorderWidth : 1f;
            var inner = Geometry.Deflate(ClientRect, new Thickness(inset));

            return new SKRect(
                inner.Left,
                MathF.Min(inner.Bottom, inner.Top + StripHeight),
                inner.Right,
                inner.Bottom);
        }
    }

    private SKRect StripViewport
    {
        get
        {
            float inset = BorderWidth > 0f ? BorderWidth : 1f;
            return new SKRect(inset, inset, ClientRect.Right - inset, inset + StripHeight);
        }
    }

    private void ApplySelection()
    {
        for (int i = 0; i < Controls.Count; i++)
        {
            Controls[i].Visible = i == _selectedIndex;
        }
    }

    private void OnPageCaptionChanged(object? sender)
    {
        _tabsDirty = true;
        Invalidate();
    }

    private void EnsureTabs()
    {
        if (!_tabsDirty)
        {
            return;
        }

        _tabsDirty = false;
        _tabRects.Clear();
        _captions.Clear();

        float inset = BorderWidth > 0f ? BorderWidth : 1f;
        float stripWidth = ClientRect.Width - (inset * 2f);
        float total = 0f;

        for (int i = 0; i < Controls.Count; i++)
        {
            var caption = Controls[i].Text;
            _captions.Add(caption);

            float width = Math.Clamp(
                TextRenderer.Measure(caption, Font, float.PositiveInfinity, false).Width + (TabPadding * 2f),
                MinTabWidth,
                MaxTabWidth);

            _tabRects.Add(new SKRect(0f, 0f, width, StripHeight));
            total += width + TabGap;
        }

        if (_tabRects.Count > 0)
        {
            total -= TabGap;
        }

        _stripOffset = Math.Clamp(_stripOffset, 0f, MathF.Max(0f, total - stripWidth));
        EnsureSelectedTabVisible(total, stripWidth);

        float x = inset - _stripOffset;
        for (int i = 0; i < _tabRects.Count; i++)
        {
            var tab = _tabRects[i];
            _tabRects[i] = new SKRect(x, inset, x + tab.Width, inset + StripHeight);
            x += tab.Width + TabGap;
        }
    }

    private void EnsureSelectedTabVisible(float total, float stripWidth)
    {
        if (_selectedIndex < 0 || _selectedIndex >= _tabRects.Count)
        {
            return;
        }

        if (total < 0f)
        {
            float inset = BorderWidth > 0f ? BorderWidth : 1f;
            stripWidth = ClientRect.Width - (inset * 2f);
            total = 0f;

            foreach (var tab in _tabRects)
            {
                total += tab.Width + TabGap;
            }
        }

        float left = 0f;

        for (int i = 0; i < _selectedIndex; i++)
        {
            left += _tabRects[i].Width + TabGap;
        }

        float right = left + _tabRects[_selectedIndex].Width;

        if (left < _stripOffset)
        {
            _stripOffset = left;
        }
        else if (right > _stripOffset + stripWidth)
        {
            _stripOffset = right - stripWidth;
        }

        _stripOffset = Math.Clamp(_stripOffset, 0f, MathF.Max(0f, total - stripWidth));
    }

    private int TabIndexFromPoint(SKPoint point)
    {
        EnsureTabs();

        for (int i = 0; i < _tabRects.Count; i++)
        {
            if (Geometry.Contains(_tabRects[i], point))
            {
                return i;
            }
        }

        return -1;
    }

    private void PaintTab(SKCanvas canvas, int index, SKRect tab)
    {
        bool selected = index == _selectedIndex;
        bool hot = index == _hotIndex && !selected && Enabled;

        if (hot)
        {
            Painter.Fill(canvas, tab, Theme.Splitter, 6f);
        }

        var color = !Enabled
            ? Theme.DisabledForeground
            : selected ? ForeColor : Theme.MutedForeground;

        TextRenderer.Draw(canvas, _captions[index], Font, color, tab, ContentAlignment.MiddleCenter);

        if (selected)
        {
            Painter.Fill(
                canvas,
                new SKRect(tab.Left + 8f, tab.Bottom - IndicatorHeight, tab.Right - 8f, tab.Bottom),
                Enabled ? Theme.Accent : Theme.DisabledForeground,
                1f);
        }
    }
}

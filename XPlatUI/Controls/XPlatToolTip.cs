using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// Shows a short line of help text when the pointer rests over a control, and takes it away when
/// the pointer leaves, the user presses a button or the delay runs out.
/// </summary>
/// <remarks>
/// <para>
/// The bubble is an <see cref="XPlatPopup"/>, so it hangs outside the owning window on a platform
/// that hands out popup windows and is painted in the window's overlay layer otherwise. A tooltip
/// never swallows the press that dismisses it, which is what makes it safe to show over a button
/// the user is about to click.
/// </para>
/// <para>
/// One instance can serve any number of controls: <see cref="SetToolTip"/> hooks the control's
/// mouse enter and leave, and removing the text unhooks it again, so nothing is left behind.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// _toolTip = new XPlatToolTip();
/// _toolTip.SetToolTip(saveButton, "Writes the file to disk.");
/// </code>
/// </example>
public sealed class XPlatToolTip : IDisposable
{
    private const float Gap = 4f;

    private readonly Dictionary<XPlatControl, ToolTipEntry> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly XPlatTimer _timer;
    private XPlatPopup? _popup;
    private IDisposable? _popupClosedToken;
    private XPlatControl? _pending;
    private int _initialDelay = 600;
    private int _autoPopDelay = 5000;
    private float _maximumWidth = 320f;
    private Phase _phase;
    private bool _active = true;
    private bool _disposed;

    public XPlatToolTip()
    {
        _timer = new XPlatTimer();
        _timer.AddTickHandler(OnTick);
    }

    private enum Phase
    {
        Idle,

        /// <summary>The pointer is over a control and the initial delay is counting down.</summary>
        Waiting,

        /// <summary>The bubble is up and the auto-pop delay is counting down.</summary>
        Showing,
    }

    /// <summary>
    /// Whether tips are shown at all. Setting this to false takes down a bubble that is up and stops
    /// the delays counting down, which is how a host suspends help text for a whole window at once.
    /// </summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value)
            {
                return;
            }

            _active = value;

            if (!value)
            {
                Hide();
            }
        }
    }

    /// <summary>
    /// How long the pointer has to rest over a control before the bubble appears, in milliseconds.
    /// </summary>
    public int InitialDelay
    {
        get => _initialDelay;
        set => _initialDelay = Math.Max(0, value);
    }

    /// <summary>
    /// How long the bubble stays up, in milliseconds, counted from when it appeared. Zero or less
    /// keeps it up until the pointer leaves.
    /// </summary>
    public int AutoPopDelay
    {
        get => _autoPopDelay;
        set => _autoPopDelay = value;
    }

    /// <summary>How wide the bubble may get before the text wraps, in logical pixels.</summary>
    public float MaximumWidth
    {
        get => _maximumWidth;
        set => _maximumWidth = MathF.Max(40f, value);
    }

    /// <summary>Whether a bubble is on screen.</summary>
    public bool IsVisible => _popup?.IsOpen == true;

    /// <summary>
    /// Sets the text shown for <paramref name="control"/>. Passing null or an empty string removes
    /// the tooltip and stops watching the control.
    /// </summary>
    public void SetToolTip(XPlatControl control, string? text)
    {
        ArgumentNullException.ThrowIfNull(control);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_entries.TryGetValue(control, out var entry))
        {
            if (!string.IsNullOrEmpty(text))
            {
                entry.Text = text;
                return;
            }

            Unhook(control, entry);
            _entries.Remove(control);

            if (_phase != Phase.Idle && _popup?.Owner == control)
            {
                Hide();
            }

            return;
        }

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        entry = new ToolTipEntry(text)
        {
            EnterToken = control.AddMouseEnterHandler(OnOwnerEnter),
            LeaveToken = control.AddMouseLeaveHandler(OnOwnerLeave),
        };

        _entries.Add(control, entry);
    }

    /// <summary>The text shown for <paramref name="control"/>, or null when it has none.</summary>
    public string? GetToolTip(XPlatControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return _entries.TryGetValue(control, out var entry) ? entry.Text : null;
    }

    /// <summary>Takes the bubble away, if it is up, and cancels a pending one.</summary>
    public void Hide() => ClosePopup();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var pair in _entries)
        {
            Unhook(pair.Key, pair.Value);
        }

        _entries.Clear();
        ClosePopup();
        _timer.Dispose();
    }

    private void OnOwnerEnter(object sender)
    {
        if (_disposed || sender is not XPlatControl control)
        {
            return;
        }

        if (!_active || !_entries.TryGetValue(control, out var entry) || !control.Enabled || !control.Visible)
        {
            return;
        }

        if (_popup is { } open && ReferenceEquals(open.Owner, control) && open.IsOpen)
        {
            return;
        }

        ClosePopup();

        if (InitialDelay <= 0)
        {
            ShowFor(control, entry.Text);
            return;
        }

        _phase = Phase.Waiting;
        _pending = control;
        _timer.Interval = TimeSpan.FromMilliseconds(InitialDelay);
        _timer.Start();
    }

    private void OnOwnerLeave(object sender)
    {
        if (sender is not XPlatControl control)
        {
            return;
        }

        if (_phase == Phase.Waiting && ReferenceEquals(_pending, control))
        {
            _pending = null;
            ClosePopup();
            return;
        }

        if (_popup is { } popup && ReferenceEquals(popup.Owner, control))
        {
            ClosePopup();
        }
    }

    private void OnTick(object sender)
    {
        if (_disposed)
        {
            return;
        }

        if (_phase == Phase.Waiting)
        {
            var control = _pending;
            _pending = null;
            _phase = Phase.Idle;

            if (control is null || !_entries.TryGetValue(control, out var entry))
            {
                ClosePopup();
                return;
            }

            // The pointer can leave a control without the host ever raising the leave - a popup
            // opening over it, for instance - so the delay re-checks what the tick is for.
            if (!control.Enabled || !control.Visible || control.Host is null || !control.IsMouseOver)
            {
                ClosePopup();
                return;
            }

            ShowFor(control, entry.Text);
            return;
        }

        if (_phase == Phase.Showing)
        {
            ClosePopup();
        }
    }

    private void ShowFor(XPlatControl control, string text)
    {
        var host = control.Host;

        if (host is null)
        {
            ClosePopup();
            return;
        }

        var view = new XPlatToolTipView(text, _maximumWidth);
        var size = view.Measure(new SKSize(MaximumWidth, float.PositiveInfinity));

        var origin = control.GetPositionInHost();
        float x = Math.Clamp(origin.X, 0f, MathF.Max(0f, host.ClientSize.Width - size.Width));
        float y = origin.Y + control.Height + Gap;

        // Below the control is the usual place; above it is where a control near the bottom edge
        // of the window has room.
        if (y + size.Height > host.ClientSize.Height && origin.Y - size.Height - Gap >= 0f)
        {
            y = origin.Y - size.Height - Gap;
        }

        var popup = new XPlatPopup(control)
        {
            Content = view,
            Size = size,

            // A tooltip is not a menu: the press that dismisses it has to reach whatever is under
            // the pointer, so it never installs a click blocker.
            DismissOnOutsideClick = false,
        };

        _popupClosedToken = popup.AddClosedHandler(OnPopupClosed);
        _popup = popup;

        try
        {
            popup.Show(new SKPoint(x, y));
        }
        catch
        {
            ClosePopup();
            throw;
        }

        if (AutoPopDelay > 0)
        {
            _phase = Phase.Showing;
            _timer.Interval = TimeSpan.FromMilliseconds(AutoPopDelay);
            _timer.Start();
        }
        else
        {
            _phase = Phase.Idle;
            _timer.Stop();
        }
    }

    private void OnPopupClosed(object sender)
    {
        if (sender is not XPlatPopup popup || !ReferenceEquals(popup, _popup))
        {
            return;
        }

        _timer.Stop();
        _phase = Phase.Idle;
        _pending = null;
        _popupClosedToken?.Dispose();
        _popupClosedToken = null;
        _popup = null;
        popup.Dispose();
    }

    private void ClosePopup()
    {
        _timer.Stop();
        _phase = Phase.Idle;
        _pending = null;

        var popup = _popup;
        var token = _popupClosedToken;
        _popup = null;
        _popupClosedToken = null;

        token?.Dispose();
        popup?.Dispose();
    }

    private void Unhook(XPlatControl control, ToolTipEntry entry)
    {
        entry.EnterToken?.Dispose();
        entry.LeaveToken?.Dispose();
        entry.EnterToken = null;
        entry.LeaveToken = null;
    }

    private sealed class ToolTipEntry
    {
        public ToolTipEntry(string text) => Text = text;

        public string Text { get; set; }

        public IDisposable? EnterToken { get; set; }

        public IDisposable? LeaveToken { get; set; }
    }
}

/// <summary>
/// The bubble a tooltip draws: a bordered, rounded surface around wrapped text.
/// </summary>
internal sealed class XPlatToolTipView : XPlatControl
{
    private readonly XPlatFont _font = XPlatTheme.DefaultFont;
    private readonly float _maximumWidth;
    private string _text;

    public XPlatToolTipView(string text, float maximumWidth)
    {
        _text = text;
        _maximumWidth = maximumWidth;

        Padding = new XPlatThickness(8f, 5f, 8f, 6f);
        CornerRadius = 5f;
        BorderWidth = 1f;
    }

    protected override SKColor? DefaultBackColor => XPlatTheme.Surface;

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        float wrapWidth = MathF.Max(0f, MathF.Min(_maximumWidth, availableSize.Width));
        var measured = XPlatTextRenderer.Measure(_text, _font, wrapWidth, wrap: true);

        return new SKSize(MathF.Ceiling(measured.Width), MathF.Ceiling(measured.Height));
    }

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        if (_text.Length == 0)
        {
            return;
        }

        var content = XPlatGeometry.Deflate(rect, Padding);
        XPlatTextRenderer.Draw(canvas, _text, _font, ForeColor, content, XPlatContentAlignment.TopLeft, wrap: true);
    }
}

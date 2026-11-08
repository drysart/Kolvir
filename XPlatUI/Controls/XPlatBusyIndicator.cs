using System.Diagnostics;
using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// An indeterminate progress spinner: a ring with a rotating arc, for work whose length is not
/// known and which a determinate <see cref="XPlatProgressBar"/> cannot describe.
/// </summary>
/// <remarks>
/// <para>
/// It takes no input and holds no focus - it is a readout, like the progress bar - so it is neither
/// a tab stop nor focusable, and it is the application that decides whether the pointer reaches what
/// is underneath it. Pair it with <see cref="XPlatOverlay"/> when the work also has to stop the user
/// from typing into the window.
/// </para>
/// <para>
/// The sweep is a timer, and it obeys the same rule a marquee progress bar does: it runs only while
/// the spinner is <see cref="XPlatControl.IsDisplayed"/> and <see cref="IsBusy"/>, and there is an
/// application to deliver its ticks. Hiding it, hiding a container around it, closing the window or
/// clearing <see cref="IsBusy"/> stops it, and an application with nothing busy runs no timer.
/// </para>
/// <para>
/// It is always a circle, whatever shape the control is: the shorter of its two sides is the
/// diameter and the ring is centred in the room that leaves, so a spinner in a wide row or a tall
/// column is round and in the middle rather than an oval stretched to the control's edges. The same
/// side sets the stroke, so a wide control does not get a heavier ring.
/// </para>
/// </remarks>
public class XPlatBusyIndicator : XPlatControl
{
    /// <summary>What the spinner uses when the platform cannot report a display rate: about 30 frames a second.</summary>
    private static readonly TimeSpan FallbackFrameInterval = TimeSpan.FromMilliseconds(30);

    private const float TurnsPerSecond = 0.9f;

    private bool _busy = true;
    private float _thickness = 3f;
    private float _phase;
    private TimeSpan? _frameInterval;
    private TimeSpan _interval = FallbackFrameInterval;
    private long _timestamp;
    private XPlatTimer? _timer;

    public XPlatBusyIndicator()
    {
        TabStop = false;
        Focusable = false;
        CornerRadius = 0f;
        Padding = XPlatThickness.Empty;
        MinimumSize = new SKSize(16f, 16f);
        Size = new SKSize(32f, 32f);
    }

    /// <summary>Whether the spinner is going round. Defaults to true, which is the state it is for.</summary>
    public bool IsBusy
    {
        get => _busy;
        set
        {
            if (_busy == value)
            {
                return;
            }

            _busy = value;
            UpdateTimer();
            Invalidate();
        }
    }

    /// <summary>The width of the ring, in logical pixels. Defaults to 3.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public float Thickness
    {
        get => _thickness;
        set
        {
            if (value <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The ring thickness must be positive.");
            }

            if (MathF.Abs(_thickness - value) < 0.01f)
            {
                return;
            }

            _thickness = value;
            Invalidate();
        }
    }

    /// <summary>
    /// How often the sweep advances. Null, the default, is the refresh rate of the display the window
    /// is on, falling back to 30 ms when the platform cannot report one.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public TimeSpan? AnimationSpeed
    {
        get => _frameInterval;
        set
        {
            if (value is { } speed && speed <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The speed must be positive.");
            }

            if (_frameInterval == value)
            {
                return;
            }

            _frameInterval = value;

            if (_timer is { Enabled: true } timer)
            {
                var interval = ResolveInterval();
                if (interval != timer.Interval)
                {
                    _interval = interval;
                    timer.Interval = interval;
                }
            }
        }
    }

    /// <summary>An auto-sized spinner is its minimum size, which is what an overlay centres.</summary>
    protected override SKSize MeasureContent(SKSize availableSize) => MinimumSize;

    /// <summary>
    /// The circle the ring is drawn on, for a client area of <paramref name="bounds"/>: the shorter
    /// side of it, centred in whichever room is left, with the stroke kept inside that circle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both axes come from the one side because a spinner is a circle. Reading the width for one and
    /// the height for the other - which is what insetting a rectangle by the stroke does - draws an
    /// oval in any control that is not square, and a spinner in a wide row or a column is not square
    /// as often as it is. The spare room is split evenly, so a circle in a rectangle sits in the
    /// middle of it rather than being stretched across it.
    /// </para>
    /// <para>
    /// The stroke is measured against that same side rather than against either edge, so a wide
    /// control does not get a heavier ring, and it is capped at half the side so the hole in the
    /// middle never closes up.
    /// </para>
    /// </remarks>
    internal static SKRect RingBounds(SKRect bounds, float thickness)
    {
        float side = MathF.Min(bounds.Width, bounds.Height);
        float stroke = MathF.Min(thickness, side / 2f);
        float radius = MathF.Max(0f, (side - stroke) / 2f);

        return new SKRect(
            bounds.MidX - radius,
            bounds.MidY - radius,
            bounds.MidX + radius,
            bounds.MidY + radius);
    }

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        UpdateTimer();

        float thickness = MathF.Min(_thickness, MathF.Min(rect.Width, rect.Height) / 2f);
        if (thickness <= 0f)
        {
            return;
        }

        var ring = RingBounds(rect, thickness);

        if (ring.Width <= 0f || ring.Height <= 0f)
        {
            return;
        }

        var track = Enabled ? XPlatTheme.Border : XPlatTheme.DisabledBackground;
        var arc = Enabled ? XPlatTheme.Accent : XPlatTheme.DisabledForeground;

        using (var paint = new SKPaint
        {
            Color = track,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness,
        })
        {
            // A circle rather than an oval: the ring is the whole shape, and a control that is not
            // square still shows a round spinner in the middle of it.
            canvas.DrawCircle(ring.MidX, ring.MidY, ring.Width / 2f, paint);
        }

        using (var paint = new SKPaint
        {
            Color = arc,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness,
            StrokeCap = SKStrokeCap.Round,
        })
        {
            // Three quarters of a turn of arc, rotated by the phase, which is what reads as motion
            // rather than as a ring that is merely incomplete.
            canvas.DrawArc(ring, _phase * 360f, 270f, false, paint);
        }
    }

    protected override void OnDisplayedChanged()
    {
        base.OnDisplayedChanged();
        UpdateTimer();
    }

    protected override void OnEnabledChanged()
    {
        base.OnEnabledChanged();
        UpdateTimer();
    }

    /// <summary>
    /// Runs the sweep only while there is something on screen to sweep and an application to tick it.
    /// </summary>
    private void UpdateTimer()
    {
        bool run = _busy && IsDisplayed && Enabled && XPlatApplication.Current is not null;

        if (!run)
        {
            _timer?.Stop();
            return;
        }

        if (_timer is null)
        {
            _interval = ResolveInterval();
            _timer = CreateTimer(_interval, OnTick);
        }
        else if (_timer.Enabled)
        {
            return;
        }
        else
        {
            _interval = ResolveInterval();
            _timer.Interval = _interval;
        }

        _timestamp = 0;
        _timer.Start();
    }

    private TimeSpan ResolveInterval()
    {
        if (_frameInterval is { } speed)
        {
            return speed;
        }

        float rate = Host?.Window?.Screen?.RefreshRate ?? 0f;
        return rate <= 0f ? FallbackFrameInterval : TimeSpan.FromMilliseconds(Math.Max(1000.0 / rate, 1.0));
    }

    private void OnTick(object sender)
    {
        // The tree belongs to the caller, so the timer can outlive the application; once that has
        // happened it ticks inline on a pool thread, which must not touch the UI.
        if (XPlatApplication.Current is null || !IsDisplayed || !_busy)
        {
            _timer?.Stop();
            return;
        }

        // Advanced by the time that actually passed rather than by the interval that was asked for,
        // so the turn takes the same time however the platform's timer behaves.
        long now = Stopwatch.GetTimestamp();
        TimeSpan elapsed = _timestamp == 0 ? _interval : Stopwatch.GetElapsedTime(_timestamp, now);
        _timestamp = now;

        _phase += (float)(elapsed.TotalSeconds * TurnsPerSecond);
        if (_phase >= 1f)
        {
            _phase -= MathF.Floor(_phase);
        }

        Invalidate();
    }
}

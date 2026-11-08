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

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        UpdateTimer();

        float thickness = MathF.Min(_thickness, MathF.Min(rect.Width, rect.Height) / 2f);
        if (thickness <= 0f)
        {
            return;
        }

        var ring = new SKRect(
            rect.Left + (thickness / 2f),
            rect.Top + (thickness / 2f),
            rect.Right - (thickness / 2f),
            rect.Bottom - (thickness / 2f));

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
            canvas.DrawOval(ring, paint);
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

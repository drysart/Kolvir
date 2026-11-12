using System.Diagnostics;
using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A progress bar: a track with a fill that shows how far <see cref="Value"/> has come between
/// <see cref="Minimum"/> and <see cref="Maximum"/>, drawn continuously, in blocks, or as a marquee
/// for work of unknown length.
/// </summary>
/// <remarks>
/// The control takes no input and holds no focus - it is a readout, like the WinForms
/// <c>ProgressBar</c>. Only a marquee bar starts a timer, and only while it is
/// <see cref="Control.IsDisplayed"/> and an <see cref="Application"/> is running the loop
/// that delivers its ticks: a bar that is hidden, inside a hidden container, or in a window that is
/// not on screen stops, and starts again when it is shown.
/// </remarks>
public class ProgressBar : Control
{
    private const float BlockWidth = 10f;
    private const float BlockGap = 2f;
    private const float MarqueeSweepsPerSecond = 0.8f;

    /// <summary>What a marquee bar uses when no display rate is available: about 33 frames a second.</summary>
    private static readonly TimeSpan FallbackMarqueeAnimationSpeed = TimeSpan.FromMilliseconds(30);

    private float _minimum;
    private float _maximum = 100f;
    private float _value;
    private float _step = 10f;
    private float _marqueePhase;
    private ProgressBarStyle _style = ProgressBarStyle.Continuous;
    private TimeSpan? _marqueeAnimationSpeed;
    private TimeSpan _marqueeInterval = FallbackMarqueeAnimationSpeed;
    private long _marqueeTimestamp;
    private Timer? _marquee;

    public ProgressBar()
    {
        CornerRadius = 4f;
        Padding = new Thickness(0f);
        MinimumSize = new SKSize(0f, 8f);
        TabStop = false;
        Focusable = false;
    }

    /// <summary>The value that fills none of the bar. Defaults to 0.</summary>
    public float Minimum
    {
        get => _minimum;
        set
        {
            if (_minimum == value)
            {
                return;
            }

            _minimum = value;
            Invalidate();
        }
    }

    /// <summary>The value that fills all of the bar. Defaults to 100.</summary>
    public float Maximum
    {
        get => _maximum;
        set
        {
            if (_maximum == value)
            {
                return;
            }

            _maximum = value;
            Invalidate();
        }
    }

    /// <summary>The current value, clamped to <see cref="Minimum"/>..<see cref="Maximum"/>.</summary>
    public float Value
    {
        get => _value;
        set
        {
            float low = MathF.Min(_minimum, _maximum);
            float high = MathF.Max(_minimum, _maximum);
            float clamped = Math.Clamp(value, low, high);

            if (_value == clamped)
            {
                return;
            }

            _value = clamped;
            Invalidate();
        }
    }

    /// <summary>How much <see cref="PerformStep"/> adds. Defaults to 10.</summary>
    public float Step
    {
        get => _step;
        set => _step = value;
    }

    /// <summary>How far along the bar is, from 0 to 1. A zero-width range reads as 0.</summary>
    public float Percent
    {
        get
        {
            float range = _maximum - _minimum;
            return range <= 0f ? 0f : Math.Clamp((_value - _minimum) / range, 0f, 1f);
        }
    }

    /// <summary>How the fill is drawn. Defaults to <see cref="ProgressBarStyle.Continuous"/>.</summary>
    public ProgressBarStyle Style
    {
        get => _style;
        set
        {
            if (_style == value)
            {
                return;
            }

            _style = value;

            // A bar that is no longer a marquee stops sweeping now; one that has become a marquee
            // starts now too, provided there is something on screen to animate.
            UpdateMarquee();
            Invalidate();
        }
    }

    /// <summary>
    /// How often a marquee bar advances. Null, the default, animates at the refresh rate of the
    /// display the window is on, and falls back to 30 ms when the platform cannot report one.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public TimeSpan? MarqueeAnimationSpeed
    {
        get => _marqueeAnimationSpeed;
        set
        {
            if (value is { } speed && speed <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The speed must be positive.");
            }

            if (_marqueeAnimationSpeed == value)
            {
                return;
            }

            _marqueeAnimationSpeed = value;

            // Setting the interval restarts a running timer, so only touch it when the resolved
            // interval really moved - a display-driven bar keeps the same one most of the time.
            if (_marquee is { Enabled: true } timer)
            {
                var interval = ResolveMarqueeInterval();
                if (interval != timer.Interval)
                {
                    _marqueeInterval = interval;
                    timer.Interval = interval;
                }
            }
        }
    }

    /// <summary>Moves <see cref="Value"/> on by <see cref="Step"/>.</summary>
    public void PerformStep() => Value += _step;

    /// <summary>Moves <see cref="Value"/> on by <paramref name="delta"/>.</summary>
    public void Increment(float delta) => Value += delta;

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        UpdateMarquee();

        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        Painter.Fill(canvas, rect, Enabled ? Theme.Secondary : Theme.DisabledBackground, CornerRadius);

        var fill = Enabled ? Theme.Accent : Theme.DisabledForeground;

        switch (_style)
        {
            case ProgressBarStyle.Marquee:
                PaintMarquee(canvas, rect, fill);
                break;

            case ProgressBarStyle.Blocks:
                PaintBlocks(canvas, rect, fill);
                break;

            default:
                PaintContinuous(canvas, rect, fill);
                break;
        }
    }

    protected override void OnDisplayedChanged()
    {
        base.OnDisplayedChanged();
        UpdateMarquee();
    }

    protected override void OnEnabledChanged()
    {
        base.OnEnabledChanged();
        UpdateMarquee();
    }

    private void PaintContinuous(SKCanvas canvas, SKRect track, SKColor color)
    {
        float width = track.Width * Percent;
        if (width < 1f)
        {
            return;
        }

        Painter.Fill(canvas, new SKRect(track.Left, track.Top, track.Left + width, track.Bottom), color, CornerRadius);
    }

    private void PaintBlocks(SKCanvas canvas, SKRect track, SKColor color)
    {
        float limit = track.Left + (track.Width * Percent);
        float radius = MathF.Min(CornerRadius, (track.Height / 2f));

        for (float left = track.Left; left < limit; left += BlockWidth + BlockGap)
        {
            float right = MathF.Min(left + BlockWidth, limit);
            if (right - left < 1f)
            {
                break;
            }

            Painter.Fill(canvas, new SKRect(left, track.Top, right, track.Bottom), color, radius);
        }
    }

    private void PaintMarquee(SKCanvas canvas, SKRect track, SKColor color)
    {
        float band = MathF.Max(24f, track.Width * 0.3f);
        float left = track.Left - band + ((track.Width + band) * _marqueePhase);

        float clippedLeft = MathF.Max(track.Left, left);
        float clippedRight = MathF.Min(track.Right, left + band);
        if (clippedRight - clippedLeft < 1f)
        {
            return;
        }

        Painter.Fill(
            canvas,
            new SKRect(clippedLeft, track.Top, clippedRight, track.Bottom),
            color,
            CornerRadius);
    }

    /// <summary>
    /// Runs the marquee clock only while the bar is on screen and there is a frame loop to drive it.
    /// </summary>
    /// <remarks>
    /// <see cref="Control.IsDisplayed"/> is what makes the decision: a bar that is hidden, sits
    /// under a hidden container, has been taken off its host, or lives in a window that was never
    /// shown or has closed has nothing to animate and stops. This is called on every paint (for a
    /// host that paints a tree it never showed) and from <see cref="OnDisplayedChanged"/> and
    /// <see cref="OnEnabledChanged"/>, which is how a bar that is not being painted - because it is
    /// not visible - still finds out that it should stop.
    /// </remarks>
    private void UpdateMarquee()
    {
        bool run = _style == ProgressBarStyle.Marquee
            && IsDisplayed
            && Enabled
            && Application.Current is not null;

        if (!run)
        {
            if (_marquee is { Enabled: true } running)
            {
                running.Stop();
            }

            return;
        }

        // The display rate is read when the sweep starts rather than on every frame: a window that
        // moved to another display, or a speed set while the bar was idle, is picked up by the next
        // start, and a running bar does not query the platform from inside its paint.
        if (_marquee is null)
        {
            _marqueeInterval = ResolveMarqueeInterval();
            _marquee = CreateTimer(_marqueeInterval, OnMarqueeTick);
        }
        else if (_marquee.Enabled)
        {
            return;
        }
        else
        {
            _marqueeInterval = ResolveMarqueeInterval();
            _marquee.Interval = _marqueeInterval;
        }

        _marqueeTimestamp = 0;
        _marquee.Start();
    }

    /// <summary>
    /// The interval one marquee frame takes: the speed the application asked for, or the display's
    /// own frame time when it asked for none.
    /// </summary>
    private TimeSpan ResolveMarqueeInterval()
    {
        if (_marqueeAnimationSpeed is { } speed)
        {
            return speed;
        }

        float rate = Host?.Window?.Screen?.RefreshRate ?? 0f;
        if (rate <= 0f)
        {
            return FallbackMarqueeAnimationSpeed;
        }

        // Floored at a millisecond so an implausible rate cannot turn the sweep into a busy loop.
        return TimeSpan.FromMilliseconds(Math.Max(1000.0 / rate, 1.0));
    }

    private void OnMarqueeTick(object sender)
    {
        // The control tree belongs to the caller, so this timer can outlive the application. Once
        // that happens Timer ticks inline on a pool thread, which must not touch the UI.
        if (Application.Current is null || !IsDisplayed)
        {
            _marquee?.Stop();
            return;
        }

        // Measured, not assumed: the platform's timer granularity rarely matches the interval it
        // was asked for, and a display-driven interval is not exact either. Advancing by the time
        // that actually passed is what keeps MarqueeSweepsPerSecond a real unit.
        long now = Stopwatch.GetTimestamp();
        TimeSpan elapsed = _marqueeTimestamp == 0
            ? _marqueeInterval
            : Stopwatch.GetElapsedTime(_marqueeTimestamp, now);

        _marqueeTimestamp = now;
        _marqueePhase += (float)(elapsed.TotalSeconds * MarqueeSweepsPerSecond);
        if (_marqueePhase >= 1f)
        {
            _marqueePhase -= MathF.Floor(_marqueePhase);
        }

        Invalidate();
    }
}

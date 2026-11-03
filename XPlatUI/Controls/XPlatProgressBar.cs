using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// A progress bar: a track with a fill that shows how far <see cref="Value"/> has come between
/// <see cref="Minimum"/> and <see cref="Maximum"/>, drawn continuously, in blocks, or as a marquee
/// for work of unknown length.
/// </summary>
/// <remarks>
/// The control takes no input and holds no focus - it is a readout, like the WinForms
/// <c>ProgressBar</c>. Only a marquee bar starts a timer, and only while it is visible and an
/// <see cref="XPlatApplication"/> is running the loop that delivers its ticks.
/// </remarks>
public class XPlatProgressBar : XPlatControl
{
    private const float BlockWidth = 10f;
    private const float BlockGap = 2f;
    private const float MarqueeSweepsPerSecond = 0.8f;

    private float _minimum;
    private float _maximum = 100f;
    private float _value;
    private float _step = 10f;
    private float _marqueePhase;
    private XPlatProgressBarStyle _style = XPlatProgressBarStyle.Continuous;
    private TimeSpan _marqueeAnimationSpeed = TimeSpan.FromMilliseconds(30);
    private XPlatTimer? _marquee;

    public XPlatProgressBar()
    {
        CornerRadius = 4f;
        Padding = new XPlatThickness(0f);
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

    /// <summary>How the fill is drawn. Defaults to <see cref="XPlatProgressBarStyle.Continuous"/>.</summary>
    public XPlatProgressBarStyle Style
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
            // starts on its next paint, when there is something on screen to animate.
            if (value != XPlatProgressBarStyle.Marquee)
            {
                _marquee?.Stop();
            }

            Invalidate();
        }
    }

    /// <summary>How often a marquee bar advances. Defaults to 30 ms.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public TimeSpan MarqueeAnimationSpeed
    {
        get => _marqueeAnimationSpeed;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The speed must be positive.");
            }

            _marqueeAnimationSpeed = value;
            if (_marquee is { Enabled: true } timer)
            {
                timer.Interval = value;
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

        XPlatPainter.Fill(canvas, rect, Enabled ? XPlatTheme.Secondary : XPlatTheme.DisabledBackground, CornerRadius);

        var fill = Enabled ? XPlatTheme.Accent : XPlatTheme.DisabledForeground;

        switch (_style)
        {
            case XPlatProgressBarStyle.Marquee:
                PaintMarquee(canvas, rect, fill);
                break;

            case XPlatProgressBarStyle.Blocks:
                PaintBlocks(canvas, rect, fill);
                break;

            default:
                PaintContinuous(canvas, rect, fill);
                break;
        }
    }

    protected override void OnVisibleChanged()
    {
        base.OnVisibleChanged();

        if (!Visible)
        {
            _marquee?.Stop();
        }
    }

    private void PaintContinuous(SKCanvas canvas, SKRect track, SKColor color)
    {
        float width = track.Width * Percent;
        if (width < 1f)
        {
            return;
        }

        XPlatPainter.Fill(canvas, new SKRect(track.Left, track.Top, track.Left + width, track.Bottom), color, CornerRadius);
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

            XPlatPainter.Fill(canvas, new SKRect(left, track.Top, right, track.Bottom), color, radius);
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

        XPlatPainter.Fill(
            canvas,
            new SKRect(clippedLeft, track.Top, clippedRight, track.Bottom),
            color,
            CornerRadius);
    }

    /// <summary>Runs the marquee clock only while there is a frame loop to drive it.</summary>
    private void UpdateMarquee()
    {
        bool run = _style == XPlatProgressBarStyle.Marquee
            && Visible
            && Host is not null
            && XPlatApplication.Current is not null;

        if (run)
        {
            _marquee ??= CreateTimer(_marqueeAnimationSpeed, OnMarqueeTick);
            if (!_marquee.Enabled)
            {
                _marquee.Start();
            }
        }
        else if (_marquee is { Enabled: true } timer)
        {
            timer.Stop();
        }
    }

    private void OnMarqueeTick(object sender)
    {
        _marqueePhase += (float)(_marqueeAnimationSpeed.TotalSeconds * MarqueeSweepsPerSecond);
        if (_marqueePhase >= 1f)
        {
            _marqueePhase -= MathF.Floor(_marqueePhase);
        }

        Invalidate();
    }
}

using SkiaSharp;

namespace XPlatUI.Animations;

/// <summary>
/// One value animated on an <see cref="XPlatAnimationSet"/>'s timeline: a start value, an end value,
/// an easing function, a start time relative to the set, a duration, and the callback that puts the
/// current value where it belongs.
/// </summary>
/// <remarks>
/// <para>
/// An animation is a description, not a running thing: it holds no clock and no state of its own
/// beyond the last value it handed over. <see cref="XPlatAnimationSet"/> owns the timeline and
/// drives every animation it contains from the same frame, which is what makes several animations
/// line up with each other instead of each drifting on its own timer.
/// </para>
/// <para>
/// The values are generic because an animation is only ever arithmetic plus a call. The
/// interpolation is a function the caller supplies - see <see cref="XPlatInterpolation"/> for the
/// ones that come with the feature - so nothing has to be discovered by reflection and a caller can
/// animate a type the framework has never heard of. The <c>Create</c> overloads here exist only to
/// make the common types read well:
/// <code>
/// set.Add(XPlatValueAnimation.Create(0f, 240f, TimeSpan.FromMilliseconds(400), v =&gt; panel.Left = v));
/// set.Add(XPlatValueAnimation.Create(SKColors.Blue, SKColors.Red, TimeSpan.FromMilliseconds(400), v =&gt; panel.BackColor = v));
/// </code>
/// </para>
/// <para>
/// The callback runs on the UI thread, once per frame, and only when the value has changed. A
/// callback that throws is reported through <see cref="XPlatApplication.AddUnhandledExceptionHandler"/>
/// rather than being allowed to unwind into the platform's timer callback, and the rest of the frame
/// still runs, so one bad animation does not freeze the others.
/// </para>
/// </remarks>
public abstract class XPlatValueAnimation
{
    internal XPlatValueAnimation(TimeSpan startTime, TimeSpan duration, Func<double, double> easing)
    {
        if (startTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startTime),
                startTime,
                "The start time is measured from the beginning of the set's timeline and cannot be negative.");
        }

        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "The duration cannot be negative. A duration of zero applies the end value instantly.");
        }

        StartTime = startTime;
        Duration = duration;
        Easing = easing;
    }

    /// <summary>
    /// When this animation begins, measured from the beginning of its set's timeline.
    /// </summary>
    public TimeSpan StartTime { get; }

    /// <summary>
    /// How long it takes. Zero means it applies its end value the moment its start time arrives.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>The moment this animation is complete, relative to the set's timeline.</summary>
    public TimeSpan EndTime => StartTime + Duration;

    /// <summary>
    /// Maps linear progress through this animation to the progress actually shown, so a non-linear
    /// value produces an ease.
    /// </summary>
    /// <remarks>
    /// It must return <c>0</c> for <c>0</c> and <c>1</c> for <c>1</c>, or the animation will not
    /// start at its start value and settle on its end value. Every curve in
    /// <see cref="XPlatEasing"/> does.
    /// </remarks>
    public Func<double, double> Easing { get; }

    /// <summary>Creates an animation of a <see cref="float"/>.</summary>
    /// <param name="from">The value when the animation begins.</param>
    /// <param name="to">The value when it ends.</param>
    /// <param name="duration">How long it takes.</param>
    /// <param name="assign">Puts the current value where it belongs.</param>
    /// <param name="startTime">When it begins, relative to the set's timeline.</param>
    /// <param name="easing">The easing curve; linear when null.</param>
    public static XPlatValueAnimation<float> Create(
        float from,
        float to,
        TimeSpan duration,
        Action<float> assign,
        TimeSpan startTime = default,
        Func<double, double>? easing = null) =>
        new(from, to, duration, assign, XPlatInterpolation.Lerp, startTime, easing);

    /// <summary>Creates an animation of a <see cref="double"/>.</summary>
    /// <param name="from">The value when the animation begins.</param>
    /// <param name="to">The value when it ends.</param>
    /// <param name="duration">How long it takes.</param>
    /// <param name="assign">Puts the current value where it belongs.</param>
    /// <param name="startTime">When it begins, relative to the set's timeline.</param>
    /// <param name="easing">The easing curve; linear when null.</param>
    public static XPlatValueAnimation<double> Create(
        double from,
        double to,
        TimeSpan duration,
        Action<double> assign,
        TimeSpan startTime = default,
        Func<double, double>? easing = null) =>
        new(from, to, duration, assign, XPlatInterpolation.Lerp, startTime, easing);

    /// <summary>Creates an animation of an <see cref="SKColor"/>.</summary>
    /// <param name="from">The value when the animation begins.</param>
    /// <param name="to">The value when it ends.</param>
    /// <param name="duration">How long it takes.</param>
    /// <param name="assign">Puts the current value where it belongs.</param>
    /// <param name="startTime">When it begins, relative to the set's timeline.</param>
    /// <param name="easing">The easing curve; linear when null.</param>
    public static XPlatValueAnimation<SKColor> Create(
        SKColor from,
        SKColor to,
        TimeSpan duration,
        Action<SKColor> assign,
        TimeSpan startTime = default,
        Func<double, double>? easing = null) =>
        new(from, to, duration, assign, XPlatInterpolation.Lerp, startTime, easing);

    /// <summary>Creates an animation of an <see cref="SKPoint"/>.</summary>
    /// <param name="from">The value when the animation begins.</param>
    /// <param name="to">The value when it ends.</param>
    /// <param name="duration">How long it takes.</param>
    /// <param name="assign">Puts the current value where it belongs.</param>
    /// <param name="startTime">When it begins, relative to the set's timeline.</param>
    /// <param name="easing">The easing curve; linear when null.</param>
    public static XPlatValueAnimation<SKPoint> Create(
        SKPoint from,
        SKPoint to,
        TimeSpan duration,
        Action<SKPoint> assign,
        TimeSpan startTime = default,
        Func<double, double>? easing = null) =>
        new(from, to, duration, assign, XPlatInterpolation.Lerp, startTime, easing);

    /// <summary>Creates an animation of an <see cref="SKSize"/>.</summary>
    /// <param name="from">The value when the animation begins.</param>
    /// <param name="to">The value when it ends.</param>
    /// <param name="duration">How long it takes.</param>
    /// <param name="assign">Puts the current value where it belongs.</param>
    /// <param name="startTime">When it begins, relative to the set's timeline.</param>
    /// <param name="easing">The easing curve; linear when null.</param>
    public static XPlatValueAnimation<SKSize> Create(
        SKSize from,
        SKSize to,
        TimeSpan duration,
        Action<SKSize> assign,
        TimeSpan startTime = default,
        Func<double, double>? easing = null) =>
        new(from, to, duration, assign, XPlatInterpolation.Lerp, startTime, easing);

    /// <summary>Creates an animation of an <see cref="SKRect"/>.</summary>
    /// <param name="from">The value when the animation begins.</param>
    /// <param name="to">The value when it ends.</param>
    /// <param name="duration">How long it takes.</param>
    /// <param name="assign">Puts the current value where it belongs.</param>
    /// <param name="startTime">When it begins, relative to the set's timeline.</param>
    /// <param name="easing">The easing curve; linear when null.</param>
    public static XPlatValueAnimation<SKRect> Create(
        SKRect from,
        SKRect to,
        TimeSpan duration,
        Action<SKRect> assign,
        TimeSpan startTime = default,
        Func<double, double>? easing = null) =>
        new(from, to, duration, assign, XPlatInterpolation.Lerp, startTime, easing);

    /// <summary>
    /// Advances one frame's worth of this animation, or does nothing when the frame is early.
    /// </summary>
    /// <param name="elapsed">How far into the set's timeline the frame is.</param>
    internal void Advance(TimeSpan elapsed)
    {
        if (elapsed < StartTime)
        {
            // Not yet begun: nothing is assigned, so the destination keeps whatever it had.
            return;
        }

        double progress = Duration <= TimeSpan.Zero
            ? 1d
            : Math.Min(1d, (elapsed - StartTime).TotalSeconds / Duration.TotalSeconds);

        ApplyProgress(Easing(progress));
    }

    /// <summary>Forgets the last value handed over, so the next frame assigns even an unchanged one.</summary>
    internal abstract void ResetApplied();

    /// <summary>Assigns the value at <paramref name="eased"/> if it differs from the last one.</summary>
    /// <param name="eased">The eased fraction of the way from the start value to the end value.</param>
    internal abstract void ApplyProgress(double eased);
}

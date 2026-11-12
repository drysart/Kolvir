namespace Kolvir.Animations;

/// <summary>
/// A value animation of <typeparamref name="T"/>, blended by the interpolation function it was
/// given.
/// </summary>
/// <typeparam name="T">The type of the animated value.</typeparam>
/// <remarks>
/// <para>
/// The interpolator is a constructor argument rather than a lookup, so this type has no registry,
/// no reflection and no static state: an animation of a type the feature has never seen is the same
/// code path as an animation of a <see cref="float"/>, and an application that animates nothing
/// keeps none of it.
/// </para>
/// <para>
/// The callback is called only when the value changes, which is what keeps an eased animation of a
/// colour or a position from invalidating its control sixty times a second for no reason. The
/// first frame always assigns, and a restarted set assigns its start value again even when it
/// matches the value it finished on.
/// </para>
/// </remarks>
public sealed class ValueAnimation<T> : ValueAnimation
{
    private readonly Func<T, T, double, T> _interpolate;
    private T _last;
    private bool _assigned;

    /// <summary>Creates an animation of <typeparamref name="T"/>.</summary>
    /// <param name="from">The value when the animation begins.</param>
    /// <param name="to">The value when it ends.</param>
    /// <param name="duration">How long it takes. Zero applies the end value as soon as the start time arrives.</param>
    /// <param name="assign">Puts the current value where it belongs. Called on the UI thread.</param>
    /// <param name="interpolate">
    /// Blends the two values by a fraction. <see cref="Interpolation"/> has the ones the
    /// framework ships; a method group such as <c>Interpolation.Lerp</c> selects the right
    /// overload from the type.
    /// </param>
    /// <param name="startTime">When it begins, relative to the set's timeline.</param>
    /// <param name="easing">The easing curve; linear when null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="assign"/> or <paramref name="interpolate"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="startTime"/> is negative, or <paramref name="duration"/> is negative.
    /// </exception>
    public ValueAnimation(
        T from,
        T to,
        TimeSpan duration,
        Action<T> assign,
        Func<T, T, double, T> interpolate,
        TimeSpan startTime = default,
        Func<double, double>? easing = null)
        : base(startTime, duration, easing ?? Kolvir.Animations.Easing.Linear)
    {
        ArgumentNullException.ThrowIfNull(assign);
        ArgumentNullException.ThrowIfNull(interpolate);

        From = from;
        To = to;
        Assign = assign;
        _interpolate = interpolate;
        _last = from;
    }

    /// <summary>The value the animation begins at.</summary>
    public T From { get; }

    /// <summary>The value the animation ends at.</summary>
    public T To { get; }

    /// <summary>The callback that puts the current value where it belongs.</summary>
    public Action<T> Assign { get; }

    /// <inheritdoc />
    internal override void ResetApplied()
    {
        _assigned = false;
        _last = From;
    }

    /// <inheritdoc />
    internal override void ApplyProgress(double eased)
    {
        T value = _interpolate(From, To, eased);

        if (_assigned && EqualityComparer<T>.Default.Equals(value, _last))
        {
            return;
        }

        // Recorded before the callback runs: a callback that throws did its own work, and retrying
        // it on every frame would turn one failure into sixty a second.
        _last = value;
        _assigned = true;

        try
        {
            Assign(value);
        }
        catch (Exception ex)
        {
            // A callback that unwinds would take the platform's timer frame with it, so a failure
            // goes where every other UI-thread failure goes; the frame carries on with the rest of
            // the set.
            Application.HandleNativeBoundary(ex);
        }
    }
}

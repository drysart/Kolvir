using System.Runtime.CompilerServices;

namespace Kolvir.Animations;

/// <summary>
/// The animation state of one expander: the duration it was given and the timeline currently moving
/// it, if any.
/// </summary>
/// <remarks>
/// <para>
/// The state sits beside the control in a weak table, so an expander that was never given a duration
/// has no entry and carries no fields for one. It wires itself to the control's own handler pair when
/// it is created; it adds nothing to the control.
/// </para>
/// <para>
/// <b>How it takes the transition over.</b> The control raises
/// <see cref="Expander.AddExpandedChangedHandler"/> before it moves
/// <see cref="Expander.ExpandProgress"/>, and leaves the progress alone if a handler assigns it
/// while the change is being raised. So the state assigns the value the section already has - which
/// holds the fold exactly where it is, halfway through a previous one included - and then drives it
/// to the new target a frame at a time. With no duration, or no clock, the state does not assign
/// anything at all and the control's own snap is the behaviour.
/// </para>
/// <para>
/// Every way out of an animation reconciles: the last frame lands exactly on 0 or 1, a duration
/// removed mid-flight settles the section on its target, and a scheduler that was never registered
/// settles it immediately. Nothing can leave a section frozen at 0.4.
/// </para>
/// </remarks>
internal sealed class ExpanderAnimationState
{
    private static readonly ConditionalWeakTable<Expander, ExpanderAnimationState> States = new();

    private readonly Expander _expander;
    private TimeSpan? _duration;
    private AnimationSet? _set;

    private ExpanderAnimationState(Expander expander)
    {
        _expander = expander;
        expander.AddExpandedChangedHandler(OnExpandedChanged);
    }

    /// <summary>Returns the state for <paramref name="expander"/>, creating and wiring it on first use.</summary>
    public static ExpanderAnimationState Get(Expander expander) =>
        States.GetValue(expander, static control => new ExpanderAnimationState(control));

    /// <summary>Returns the state for <paramref name="expander"/>, or null when it never opted in.</summary>
    public static ExpanderAnimationState? Find(Expander expander) =>
        States.TryGetValue(expander, out var state) ? state : null;

    /// <summary>How long the fold takes, or null to jump.</summary>
    public TimeSpan? Duration
    {
        get => _duration;
        set
        {
            if (value is { } duration && duration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The duration cannot be negative.");
            }

            if (_duration == value)
            {
                return;
            }

            _duration = value;

            // Removing the duration cannot leave the fold it started in the air: whatever the target
            // is now, that is where the section belongs.
            if (value is null)
            {
                Settle();
            }
        }
    }

    /// <summary>
    /// Runs on every change of <see cref="Expander.IsExpanded"/>: holds the progress where it is
    /// so the control does not snap it, then animates it to the new target.
    /// </summary>
    private void OnExpandedChanged(object sender)
    {
        if (_duration is not { } duration)
        {
            return;
        }

        double from = _expander.ExpandProgress;
        double to = _expander.IsExpanded ? 1d : 0d;

        // Assigning the value it already has is what counts as taking the transition over - the
        // control checks whether anything wrote the progress, not whether it moved.
        _expander.ExpandProgress = from;

        Animate(from, to, duration);
    }

    private void Animate(double from, double to, TimeSpan duration)
    {
        Stop();

        if (AnimationScheduler.FindRegistered() is not { } scheduler)
        {
            // The application never called AddAnimations(), so there is no clock and no point in
            // pretending: the section goes where it was asked to go, at once.
            _expander.ExpandProgress = to;
            return;
        }

        var set = new AnimationSet(scheduler);
        set.Add(ValueAnimation.Create(
            from,
            to,
            duration,
            value => _expander.ExpandProgress = value,
            easing: Easing.EaseInOutQuad));

        set.AddFinishedHandler(_ => Settle());

        _set = set;

        // A zero duration finishes inside Start, so by the time this returns the set has already
        // settled the section and released itself.
        set.Start();
    }

    /// <summary>Stops whatever is moving and puts the section exactly on its target.</summary>
    private void Settle()
    {
        Stop();
        _expander.ExpandProgress = _expander.IsExpanded ? 1d : 0d;
    }

    private void Stop()
    {
        var set = _set;
        _set = null;
        set?.Dispose();
    }
}

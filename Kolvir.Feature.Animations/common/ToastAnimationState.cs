using System.Runtime.CompilerServices;

namespace Kolvir.Animations;

/// <summary>
/// The animation state of one toast: the two durations it was given and the timeline currently
/// moving it, if any.
/// </summary>
/// <remarks>
/// <para>
/// The state sits beside the control in a weak table, so a toast that was never given a duration has
/// no entry and carries no fields for one. It wires itself to the control's own handler pair when it
/// is created; it adds nothing to the control.
/// </para>
/// <para>
/// <b>How it takes the transition over.</b> The control raises
/// <see cref="Toast.AddShowingHandler"/> and <see cref="Toast.AddDismissingHandler"/> before
/// it moves <see cref="Toast.ShowProgress"/>, and leaves the progress alone if a handler assigns
/// it while the change is being raised. So the state assigns the value the message already has -
/// which holds it exactly where it is, halfway through the other transition included - and then
/// drives it to the target a frame at a time. With no duration, or no clock, the state assigns
/// nothing at all and the control's own snap is the behaviour.
/// </para>
/// <para>
/// Every way out of an animation reconciles: the last frame lands exactly on 1 or 0, a duration
/// removed mid-flight settles the message on its target, and a scheduler that was never registered
/// settles it immediately. For a dismissal that target is 0, which is the control's own signal to
/// take the message off the screen - so an interrupted or cancelled dismissal still ends with the
/// toast gone rather than faded halfway and stranded.
/// </para>
/// </remarks>
internal sealed class ToastAnimationState
{
    private static readonly ConditionalWeakTable<Toast, ToastAnimationState> States = new();

    private readonly Toast _toast;
    private TimeSpan? _showDuration;
    private TimeSpan? _dismissDuration;
    private AnimationSet? _set;

    private ToastAnimationState(Toast toast)
    {
        _toast = toast;
        toast.AddShowingHandler(OnShowing);
        toast.AddDismissingHandler(OnDismissing);
    }

    /// <summary>Returns the state for <paramref name="toast"/>, creating and wiring it on first use.</summary>
    public static ToastAnimationState Get(Toast toast) =>
        States.GetValue(toast, static control => new ToastAnimationState(control));

    /// <summary>Returns the state for <paramref name="toast"/>, or null when it never opted in.</summary>
    public static ToastAnimationState? Find(Toast toast) =>
        States.TryGetValue(toast, out var state) ? state : null;

    /// <summary>How long the message takes to appear, or null to appear at once.</summary>
    public TimeSpan? ShowDuration
    {
        get => _showDuration;
        set
        {
            Validate(value);

            if (_showDuration == value)
            {
                return;
            }

            _showDuration = value;

            // Removing the duration cannot leave the transition it started in the air.
            if (value is null)
            {
                Settle();
            }
        }
    }

    /// <summary>How long the message takes to go, or null to go at once.</summary>
    public TimeSpan? DismissDuration
    {
        get => _dismissDuration;
        set
        {
            Validate(value);

            if (_dismissDuration == value)
            {
                return;
            }

            _dismissDuration = value;

            // Removing it mid-dismissal is a cancellation, and a cancellation still ends with the
            // message gone: settling on the target is what hands the removal to the control.
            if (value is null)
            {
                Settle();
            }
        }
    }

    private static void Validate(TimeSpan? value)
    {
        if (value is { } duration && duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The duration cannot be negative.");
        }
    }

    /// <summary>
    /// Runs on every <see cref="Toast.Show"/>: holds the progress where it is so the control
    /// does not snap it, then animates it to 1.
    /// </summary>
    private void OnShowing(object sender) => Animate(_showDuration, 1d);

    /// <summary>
    /// Runs on every <see cref="Toast.Dismiss"/>: holds the progress where it is so the control
    /// does not snap it, then animates it to 0 and lets the control do the removal when it arrives.
    /// </summary>
    private void OnDismissing(object sender) => Animate(_dismissDuration, 0d);

    private void Animate(TimeSpan? duration, double target)
    {
        if (duration is not { } value)
        {
            return;
        }

        double from = _toast.ShowProgress;

        // Assigning the value it already has is what counts as taking the transition over - the
        // control checks whether anything wrote the progress, not whether it moved.
        _toast.ShowProgress = from;

        Run(from, target, value);
    }

    private void Run(double from, double to, TimeSpan duration)
    {
        Stop();

        if (AnimationScheduler.FindRegistered() is not { } scheduler)
        {
            // The application never called AddAnimations(), so there is no clock and no point in
            // pretending: the message goes where it was asked to go, at once.
            _toast.ShowProgress = to;
            return;
        }

        var set = new AnimationSet(scheduler);
        set.Add(ValueAnimation.Create(
            from,
            to,
            duration,
            value => _toast.ShowProgress = value,
            easing: Easing.EaseInOutQuad));

        set.AddFinishedHandler(_ => Settle());

        _set = set;

        // A zero duration finishes inside Start, so by the time this returns the set has already
        // settled the message and released itself.
        set.Start();
    }

    /// <summary>Stops whatever is moving and puts the message exactly where it belongs.</summary>
    private void Settle()
    {
        Stop();
        _toast.ShowProgress = _toast.IsOpen ? 1d : 0d;
    }

    private void Stop()
    {
        var set = _set;
        _set = null;
        set?.Dispose();
    }
}

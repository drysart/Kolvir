using System.Runtime.CompilerServices;

namespace XPlatUI.Animations;

/// <summary>
/// The animation state of one overlay: the duration it was given and the timeline currently fading
/// it in, if any.
/// </summary>
/// <remarks>
/// <para>
/// The state sits beside the control in a weak table, so a layer that was never given a duration has
/// no entry and carries no fields for one. It wires itself to the control's displayed-change handler
/// pair when it is created, which is the seam that says a layer has just come up or just gone away
/// without the layer having to know that anything is animating it.
/// </para>
/// <para>
/// <b>Only displayed starts a fade.</b> A layer that is not <see cref="XPlatControl.IsDisplayed"/>
/// cannot be painted and must not hold a clock - the rule the marquee bar set - so becoming hidden
/// stops the transition and puts the layer back at its resting opacity. That is also what keeps a
/// duration removed mid-fade from stranding a half-transparent layer.
/// </para>
/// <para>
/// The opacity is the layer's own <see cref="XPlatControl.Opacity"/>, which already defaults to
/// fully opaque, clamps what it is given, and composites the layer and its children as one. The
/// state only drives it and puts it back.
/// </para>
/// </remarks>
internal sealed class XPlatOverlayAnimationState
{
    private static readonly ConditionalWeakTable<XPlatOverlay, XPlatOverlayAnimationState> States = new();

    private readonly XPlatOverlay _overlay;
    private TimeSpan? _fadeDuration;
    private XPlatAnimationSet? _set;

    private XPlatOverlayAnimationState(XPlatOverlay overlay)
    {
        _overlay = overlay;
        overlay.AddDisplayedChangedHandler(OnDisplayedChanged);
    }

    /// <summary>Returns the state for <paramref name="overlay"/>, creating and wiring it on first use.</summary>
    public static XPlatOverlayAnimationState Get(XPlatOverlay overlay) =>
        States.GetValue(overlay, static control => new XPlatOverlayAnimationState(control));

    /// <summary>Returns the state for <paramref name="overlay"/>, or null when it never opted in.</summary>
    public static XPlatOverlayAnimationState? Find(XPlatOverlay overlay) =>
        States.TryGetValue(overlay, out var state) ? state : null;

    /// <summary>How long the layer takes to come in, or null to appear at once.</summary>
    public TimeSpan? FadeDuration
    {
        get => _fadeDuration;
        set
        {
            if (value is { } duration && duration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The duration cannot be negative.");
            }

            if (_fadeDuration == value)
            {
                return;
            }

            _fadeDuration = value;

            // Removing it mid-fade puts the layer at its full dimming at once rather than leaving it
            // stopped part way in.
            if (value is null)
            {
                Settle();
            }
        }
    }

    /// <summary>
    /// Runs when the layer started or stopped being visible to the user, which is the only thing
    /// that begins or ends a fade.
    /// </summary>
    private void OnDisplayedChanged(object sender)
    {
        if (_overlay.IsDisplayed)
        {
            FadeIn();
            return;
        }

        // Nothing can see the layer, so nothing should be spending a clock on it, and the next time
        // it comes up it starts from the beginning rather than from wherever this one stopped.
        Settle();
    }

    private void FadeIn()
    {
        if (_fadeDuration is not { } duration)
        {
            return;
        }

        Stop();

        if (XPlatAnimationScheduler.FindRegistered() is not { } scheduler)
        {
            // No AddAnimations() means no clock, and a layer left part way in would be worse than
            // one that simply appears.
            _overlay.Opacity = 1f;
            return;
        }

        var set = new XPlatAnimationSet(scheduler);
        set.Add(XPlatValueAnimation.Create(
            0f,
            1f,
            duration,
            value => _overlay.Opacity = value,
            easing: XPlatEasing.EaseOutQuad));

        set.AddFinishedHandler(_ => Settle());

        _set = set;

        // A zero duration finishes inside Start, so by the time this returns the layer is already at
        // its full dimming and the set has released itself.
        set.Start();
    }

    /// <summary>Stops whatever is fading and puts the layer at its resting opacity.</summary>
    private void Settle()
    {
        Stop();
        _overlay.Opacity = 1f;
    }

    private void Stop()
    {
        var set = _set;
        _set = null;
        set?.Dispose();
    }
}

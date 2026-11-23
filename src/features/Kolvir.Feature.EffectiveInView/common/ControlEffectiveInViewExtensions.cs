namespace Kolvir.InView;

/// <summary>
/// The members a control gains by opting into the effective-in-view feature: whether some part of it
/// is genuinely on screen, and the handlers that hear when that changes.
/// </summary>
/// <remarks>
/// <para>
/// These are extension members rather than members of <see cref="Control"/>, the way
/// FEATURE-COST-POLICY.md section 5 asks, and the surface still reads as part of the control:
/// <code>
/// using Kolvir.InView;
///
/// using var subscription = control.AddEffectiveInViewChangedHandler(sender => Update((Control)sender));
/// bool seen = control.EffectiveInView;
/// </code>
/// </para>
/// <para>
/// <b>What it answers.</b> <see cref="Control.EffectiveVisible"/> is the cheap question - the control
/// and every container above it have their <see cref="Control.Visible"/> flag set. This is the
/// expensive one: the control's rectangle is clipped by every ancestor's content bounds, then reduced
/// by everything painted over it, and the answer is true when anything survives. A control scrolled
/// out of a viewport, moved outside its parent, covered by an opaque control above it, or under a
/// hidden ancestor answers false; one that is partly scrolled out, partly covered or partly off the
/// edge answers true.
/// </para>
/// <para>
/// <b>What it does not answer.</b> It is a question about the geometry the last layout produced, not
/// about the window manager: it does not require the window to be shown, so an unshown or headless
/// window still answers, and an application that needs "and the window is up" combines it with
/// <see cref="Control.IsDisplayed"/>. Occlusion is only as good as <see cref="Control.Opaque"/>: a
/// control that paints opaque content on a transparent background - an <see cref="Image"/>, a custom
/// canvas - is not reported as covering anything, and a rounded, faded or filtered control is treated
/// as if its corners were square. dev-docs/EFFECTIVE-IN-VIEW.md lists every case; the honest summary
/// is that a false answer is always worth believing and a true answer can be too generous where a
/// control covers without being opaque.
/// </para>
/// <para>
/// <b>What it costs.</b> Nothing until it is read or watched. A read computes from the layout that has
/// already run and caches the answer until the next pass, so a loop that asks the same control many
/// times in one frame computes once. A control with a handler is recomputed by the pass itself, and a
/// pass that moved nothing still costs one recomputation per watched control - not a walk of the tree
/// per control.
/// </para>
/// </remarks>
public static class ControlEffectiveInViewExtensions
{
    extension(Control control)
    {
        /// <summary>
        /// True when some part of the control is genuinely on screen: it survives the clipping of
        /// every container above it and nothing opaque painted over it covers the whole of it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The answer is computed from the last completed layout pass and cached until the next one.
        /// A scroll moves content without laying out, so a scroll is a pass as far as this property is
        /// concerned: the cache is dropped and a watched control is recomputed.
        /// </para>
        /// <para>
        /// This never throws. A control that is not attached to a host, or whose host has not been
        /// laid out, answers false - there is no host geometry for it to be visible in yet - and so
        /// does a control that was detached while a pass was running.
        /// </para>
        /// <para>
        /// It is a UI-thread question, like every other read of the control tree.
        /// </para>
        /// </remarks>
        public bool EffectiveInView => EffectiveInViewState.Read(control);

        /// <summary>
        /// Adds a handler that runs when <c>EffectiveInView</c> changed during a completed layout
        /// pass.
        /// </summary>
        /// <param name="handler">
        /// The handler to add. It is passed the control whose answer changed; reading the property
        /// again returns the new value.
        /// </param>
        /// <returns>A token that removes the handler when it is disposed.</returns>
        /// <remarks>
        /// <para>
        /// The first pass after registering establishes the baseline and does not call the handler, so
        /// adding a handler to a control that is already on screen does not look like a change.
        /// Handlers run after the pass has finished arranging, in subscription order; one that throws
        /// stops those after it and the exception reaches
        /// <see cref="Application.AddUnhandledExceptionHandler"/> through the caller's guard.
        /// </para>
        /// <para>
        /// A handler must not expect a second pass from the same change: one that moves a control and
        /// lays it out does complete that pass, but it runs no handlers of its own. The next pass
        /// reports the result. Handlers do not run for a read, so reading the property from a timer or
        /// an animation never fires them.
        /// </para>
        /// </remarks>
        public IDisposable AddEffectiveInViewChangedHandler(Action<object> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            return EffectiveInViewState.AddHandler(control, handler);
        }

        /// <summary>Removes a handler that <see cref="AddEffectiveInViewChangedHandler"/> added.</summary>
        /// <param name="handler">The handler to remove.</param>
        public void RemoveEffectiveInViewChangedHandler(Action<object> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            EffectiveInViewState.RemoveHandler(control, handler);
        }
    }
}

namespace Kolvir.Animations;

/// <summary>
/// What an <see cref="Toast"/> gains by opting into the feature: a duration for appearing and a
/// duration for going away.
/// </summary>
/// <remarks>
/// <para>
/// Extension members rather than properties on the control, the way FEATURE-COST-POLICY.md §5 asks
/// and the way <see cref="ExpanderAnimationExtensions.ExpandDuration"/> does it: the state lives
/// in a table keyed by the toast, so an application that never asks for the transition carries none
/// of it, and a message that never animates is the same control it was.
/// </para>
/// <para>
/// Both durations drive the one number the message is drawn from -
/// <see cref="Toast.ShowProgress"/>, which is its opacity and how far it has come in from the
/// edge it is docked against - so the fade and the slide cannot drift apart, and there is no second
/// timeline to keep in step.
/// </para>
/// <code>
/// toast.ShowDuration = TimeSpan.FromMilliseconds(180);
/// toast.DismissDuration = TimeSpan.FromMilliseconds(140);
///
/// toast.Show();      // fades and slides in, then counts down
/// toast.Dismiss();   // reverses, and only then leaves the layout
/// </code>
/// <para>
/// Paying for it is voluntary twice over: the assembly is a package the application references, and
/// the clock it runs on only exists once the application calls <c>AddAnimations()</c>. With a
/// duration set and no scheduler registered the message snaps, which is what it does with no
/// duration at all - it is never left half faded waiting for a clock that will not arrive.
/// </para>
/// </remarks>
public static class ToastAnimationExtensions
{
    extension(Toast toast)
    {
        /// <summary>
        /// How long the message takes to appear, or null - the default - to appear at once.
        /// </summary>
        /// <remarks>
        /// The message takes its room in the layout straight away and comes in over it, so nothing
        /// below it moves until it is gone. Setting this while the message is already up moves
        /// nothing: there is no appearance left to animate, and the next <see cref="Toast.Show"/>
        /// uses it.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
        public TimeSpan? ShowDuration
        {
            get => ToastAnimationState.Find(toast)?.ShowDuration;
            set => ToastAnimationState.Get(toast).ShowDuration = value;
        }

        /// <summary>
        /// How long the message takes to go, or null - the default - to go at once.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The message stays on the screen until this finishes: it is taken out of the layout and
        /// <see cref="Toast.AddDismissedHandler"/> runs at the end of the transition rather than
        /// at the call that started it. It has already left the Tab order and given up the focus by
        /// then, so it cannot be reached on its way out.
        /// </para>
        /// <para>
        /// Setting this to null while a dismissal is in flight ends the transition and takes the
        /// message off the screen at once, so a cancelled animation can never leave a half-faded
        /// toast stuck over the page.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
        public TimeSpan? DismissDuration
        {
            get => ToastAnimationState.Find(toast)?.DismissDuration;
            set => ToastAnimationState.Get(toast).DismissDuration = value;
        }
    }
}

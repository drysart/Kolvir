namespace Kolvir.Animations;

/// <summary>
/// What an <see cref="Expander"/> gains by opting into the feature: a duration for its expand
/// and collapse.
/// </summary>
/// <remarks>
/// <para>
/// An extension member rather than a property on the control, the way FEATURE-COST-POLICY.md §5
/// asks: the state lives in a table keyed by the expander, so an application that never asks for the
/// animation carries none of it, and a section that never animates is the same control it was.
/// </para>
/// <para>
/// The duration animates the one number the control draws itself from -
/// <see cref="Expander.ExpandProgress"/>, which moves both the content height and the
/// chevron's rotation - so there is no second timeline that could disagree with the first, and the
/// fold the user sees mid-flight is exactly what <c>IsExpanded</c> says the section is heading for.
/// </para>
/// <code>
/// appearance.ExpandDuration = TimeSpan.FromMilliseconds(180);
/// appearance.IsExpanded = false;   // folds over 180 ms, arrow and height together
/// </code>
/// <para>
/// Paying for it is voluntary twice over: the assembly is a package the application references, and
/// the clock it runs on only exists once the application calls <c>AddAnimations()</c>. With the
/// duration set and no scheduler registered the section snaps, which is what it does with no
/// duration at all - it is never left half open waiting for a clock that will not arrive.
/// </para>
/// </remarks>
public static class ExpanderAnimationExtensions
{
    extension(Expander expander)
    {
        /// <summary>
        /// How long expanding and collapsing take, or null - the default - to jump as the control
        /// does on its own.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A change of the target while it is still moving starts the new fold from wherever the
        /// section has got to, so collapsing halfway through an expand turns round rather than
        /// jumping open first. Setting it to null while a fold is in flight stops the animation and
        /// puts the section exactly where <see cref="Expander.IsExpanded"/> says it belongs.
        /// Setting it on a section that is already where it belongs moves nothing - there is no fold
        /// to animate - so the next change of the target uses it.
        /// </para>
        /// <para>
        /// Zero is allowed and means "go straight there", which reads as a jump but still reconciles
        /// through the same path as any other duration.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
        public TimeSpan? ExpandDuration
        {
            get => ExpanderAnimationState.Find(expander)?.Duration;
            set => ExpanderAnimationState.Get(expander).Duration = value;
        }
    }
}

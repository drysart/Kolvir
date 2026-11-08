namespace XPlatUI.Animations;

/// <summary>
/// What an <see cref="XPlatOverlay"/> gains by opting into the feature: a duration for coming in.
/// </summary>
/// <remarks>
/// <para>
/// An extension member rather than a property on the control, the same shape
/// <see cref="XPlatExpanderAnimationExtensions.ExpandDuration"/> and
/// <see cref="XPlatToastAnimationExtensions.ShowDuration"/> take: the state lives in a table keyed
/// by the layer, so an application that never asks for the fade carries none of it.
/// </para>
/// <para>
/// The fade is <see cref="XPlatControl.Opacity"/> on the layer, so the dimming and whatever is
/// inside it - the spinner, the card, the message - come in as one composited picture rather than
/// each blending with what is under it. It starts when the layer becomes displayed and lands
/// exactly on fully opaque; the layer is already taking presses and keys while it comes in, so
/// "blocking, and arriving" is never "half blocking".
/// </para>
/// <code>
/// overlay.FadeDuration = TimeSpan.FromMilliseconds(120);
/// overlay.Visible = true;   // dims in over the panel it covers
/// </code>
/// <para>
/// There is no fade out, and that is deliberate: the layer is taken off the screen by assigning
/// <see cref="XPlatControl.Visible"/>, and a fade out would mean the control holding on to a
/// hidden layer - keeping the application's word for what is on screen from being true until a
/// clock it may not have finished. Coming in is the transition that carries the meaning here.
/// </para>
/// </remarks>
public static class XPlatOverlayAnimationExtensions
{
    extension(XPlatOverlay overlay)
    {
        /// <summary>
        /// How long the layer takes to come in, or null - the default - to appear at once.
        /// </summary>
        /// <remarks>
        /// <para>
        /// It applies to the next time the layer becomes displayed, which is what showing a hidden
        /// layer does. Setting it on a layer that is already up moves nothing - there is nothing
        /// left to fade in - and setting it back to null while the fade is in flight puts the layer
        /// at its full dimming at once.
        /// </para>
        /// <para>
        /// Zero is allowed and means "go straight there", which reads as a jump but reconciles
        /// through the same path as any other duration.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
        public TimeSpan? FadeDuration
        {
            get => XPlatOverlayAnimationState.Find(overlay)?.FadeDuration;
            set => XPlatOverlayAnimationState.Get(overlay).FadeDuration = value;
        }
    }
}

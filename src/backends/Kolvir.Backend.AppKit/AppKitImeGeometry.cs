using CoreGraphics;
using SkiaSharp;

namespace Kolvir.Backend.AppKit;

/// <summary>
/// The one conversion from the caret seam to the space a macOS input method wants.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="UiHost.TryGetCaretBounds"/> answers in host coordinates, which are points measured
/// down from the top. <c>firstRectForCharacterRange:actualRange:</c> wants screen coordinates,
/// which are points measured up from the bottom. So the Y axis is flipped against the view's
/// height and the view's own bottom edge on the screen is added - and the display's backing scale
/// is <em>not</em> applied, because neither side of this conversion is measured in device pixels.
/// </para>
/// <para>
/// That last point is worth stating plainly, because it is easy to get wrong and silently wrong:
/// multiplying by the backing scale would answer a rectangle twice as far from the window's corner
/// on a Retina display, and the candidate window would hang beside the field instead of under the
/// caret. <c>AppKitImeGeometryTests</c> pins the arithmetic, and
/// <c>tests/backends/Kolvir.Backend.AppKit.Harness</c> compares the shipped answer with
/// <c>NSWindow.ConvertRectToScreen</c> on a 2x display.
/// </para>
/// <para>
/// There is deliberately one such conversion in this backend. macOS asks the text input client for
/// the caret through <c>firstRectForCharacterRange:</c> alone - there is no separate composition
/// form to keep in step the way Win32 has <c>ImmSetCompositionWindow</c> next to
/// <c>IMR_QUERYCHARPOSITION</c> - so the candidate window and the character viewer cannot be
/// anchored from two different rectangles.
/// </para>
/// </remarks>
internal static class AppKitImeGeometry
{
    /// <summary>
    /// Converts a caret rectangle in host coordinates into the screen rectangle AppKit expects.
    /// </summary>
    /// <param name="caret">The caret, in host points measured down from the top of the view.</param>
    /// <param name="viewHeight">The height of the view the caret was measured in, in points.</param>
    /// <param name="viewBottomLeftOnScreen">
    /// The view's bottom-left corner in screen points, whatever the view's own orientation is.
    /// <see cref="AppKitSkiaView"/> is flipped, so its bottom edge is the one at the view's own
    /// height.
    /// </param>
    /// <returns>The caret rectangle in screen points.</returns>
    public static CGRect ToScreenRect(SKRect caret, double viewHeight, CGPoint viewBottomLeftOnScreen)
    {
        double height = Math.Max(0d, caret.Height);

        return new CGRect(
            viewBottomLeftOnScreen.X + caret.Left,
            viewBottomLeftOnScreen.Y + (viewHeight - caret.Bottom),
            caret.Width,
            height);
    }
}

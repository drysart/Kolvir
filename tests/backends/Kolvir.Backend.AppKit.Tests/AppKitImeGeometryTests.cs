using CoreGraphics;
using SkiaSharp;

namespace Kolvir.Backend.AppKit.Tests;

/// <summary>
/// The caret answer macOS uses to place a candidate window or the character viewer, proved
/// numerically.
/// </summary>
/// <remarks>
/// <para>
/// The conversion is the whole of it: flip the Y axis against the view's height, add the view's
/// bottom edge on the screen, and do <em>not</em> multiply by the backing scale. Two of those three
/// are easy to state and the third is the one that goes wrong quietly, so the numbers below are the
/// assertion. They are also what the documentation quotes. That the answer is in points - and so
/// unchanged on a 2x display - is proved live rather than here:
/// <c>tests/backends/Kolvir.Backend.AppKit.Harness</c> compares the shipped answer with
/// <c>NSWindow.ConvertRectToScreen</c> and prints the backing scale it ran at.
/// </para>
/// <para>
/// The view is 200 points tall and its bottom edge is at screen <c>(100, 50)</c>. The caret is at
/// <c>(10, 20, 12, 40)</c> in host points, measured from the top: its bottom edge is 40 points from
/// the top, so 160 above the view's bottom, and its top edge is 20 from the top, so 180. The answer
/// is therefore X <c>110</c> and Y <c>210</c> to <c>230</c>.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Platform.AppKit")]
public sealed class AppKitImeGeometryTests
{
    private static readonly SKRect Caret = new(10f, 20f, 12f, 40f);

    private static readonly CGPoint ViewBottomLeft = new(100d, 50d);

    [TestMethod]
    public void ACaretIsFlippedAgainstTheViewHeightAndOffsetByTheBottomEdge()
    {
        var rect = AppKitImeGeometry.ToScreenRect(Caret, 200d, ViewBottomLeft);

        Assert.AreEqual(110d, (double)rect.Left, 0.0001d, "X is the view's left edge plus the caret's X");
        Assert.AreEqual(210d, (double)rect.Top, 0.0001d, "the caret's bottom edge is 200 - 40 above the view's bottom");
        Assert.AreEqual(112d, (double)rect.Right, 0.0001d);
        Assert.AreEqual(230d, (double)rect.Bottom, 0.0001d, "the caret's top edge is 200 - 20 above the view's bottom");
        Assert.AreEqual(2d, (double)rect.Width, 0.0001d);
        Assert.AreEqual(20d, (double)rect.Height, 0.0001d);
    }

    [TestMethod]
    public void ACaretOnTheFirstLineSitsAtTheTopOfTheView()
    {
        // The view's top edge is 200 points above its bottom edge, at screen 250. A caret on the
        // first line occupies the top 20 points of the view, so its top edge is screen 230 and its
        // bottom edge is 250 - not the other way round.
        var rect = AppKitImeGeometry.ToScreenRect(new SKRect(0f, 0f, 1f, 20f), 200d, ViewBottomLeft);

        Assert.AreEqual(230d, (double)rect.Top, 0.0001d);
        Assert.AreEqual(250d, (double)rect.Bottom, 0.0001d);
    }

    [TestMethod]
    public void ACaretOnTheLastLineSitsOnTheBottomEdge()
    {
        var rect = AppKitImeGeometry.ToScreenRect(new SKRect(0f, 180f, 1f, 200f), 200d, ViewBottomLeft);

        Assert.AreEqual(50d, (double)rect.Top, 0.0001d, "the last line's bottom edge is the view's bottom");
        Assert.AreEqual(70d, (double)rect.Bottom, 0.0001d);
    }

    [TestMethod]
    public void ACaretInASecondWindowUsesThatWindowsOrigin()
    {
        // A popup or a dialog is a window of its own, and the same caret must follow it.
        var rect = AppKitImeGeometry.ToScreenRect(Caret, 200d, new CGPoint(700d, 400d));

        Assert.AreEqual(710d, (double)rect.Left, 0.0001d);
        Assert.AreEqual(560d, (double)rect.Top, 0.0001d);
    }
}

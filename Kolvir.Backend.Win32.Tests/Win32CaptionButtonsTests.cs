using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using Kolvir;

namespace Kolvir.Backend.Win32.Tests;

/// <summary>
/// The geometry of the emulated caption buttons: where the three buttons sit, what the strip an
/// application must keep clear is, and which button a client point belongs to.
/// </summary>
/// <remarks>
/// These are the numbers the application sees through
/// <see cref="WindowChromeMetrics.CaptionButtonBounds"/>, so a mistake here is visible to the
/// application as wrong reserved space or a dead strip, and to the backend as a press that goes to the
/// wrong button. The strip is asserted as a union rather than a constant because the width is the
/// button count times the button size, and the button count is a platform decision.
/// </remarks>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32CaptionButtonsTests
{
    private const float ClientWidth = 800f;
    private const float StripHeight = 21.5f;
    private const float ButtonWidth = 46f;

    [TestMethod]
    public void LayoutOrdersTheButtonsLeftToRightAtTheRightEdge()
    {
        var (strip, buttons) = Win32CaptionButtons.Layout(ClientWidth, StripHeight, maximized: false);

        Assert.AreEqual(3, buttons.Count);
        Assert.AreEqual(CaptionButtonKind.Minimize, buttons[0].Kind);
        Assert.AreEqual(CaptionButtonKind.Maximize, buttons[1].Kind);
        Assert.AreEqual(CaptionButtonKind.Close, buttons[2].Kind);

        Assert.AreEqual(ClientWidth, buttons[2].Bounds.Right, 0.001f, "close is flush with the client edge");
        Assert.AreEqual(ClientWidth - ButtonWidth, buttons[2].Bounds.Left, 0.001f);
        Assert.AreEqual(buttons[2].Bounds.Left, buttons[1].Bounds.Right, 0.001f, "no gap between buttons");
        Assert.AreEqual(buttons[1].Bounds.Left, buttons[0].Bounds.Right, 0.001f);
        Assert.AreEqual(0f, buttons[2].Bounds.Top, 0.001f);
        Assert.AreEqual(StripHeight, buttons[2].Bounds.Bottom, 0.001f);
    }

    [TestMethod]
    public void MaximisedLayoutShowsRestoreInsteadOfMaximize()
    {
        var (_, restored) = Win32CaptionButtons.Layout(ClientWidth, StripHeight, maximized: false);
        var (_, maximized) = Win32CaptionButtons.Layout(ClientWidth, StripHeight, maximized: true);

        Assert.AreEqual(CaptionButtonKind.Maximize, restored[1].Kind);
        Assert.AreEqual(CaptionButtonKind.Restore, maximized[1].Kind);
        Assert.IsFalse(maximized.Any(b => b.Kind == CaptionButtonKind.Maximize),
            "a maximised window reports one button or the other, not both");
        Assert.AreEqual(restored[1].Bounds, maximized[1].Bounds, "the button stays where it was");
    }

    [TestMethod]
    public void StripIsTheUnionOfEveryButton()
    {
        var (strip, buttons) = Win32CaptionButtons.Layout(ClientWidth, StripHeight, maximized: false);

        foreach (var button in buttons)
        {
            Assert.IsTrue(strip.Contains(button.Bounds), $"{button.Kind} must lie inside the strip");
        }

        Assert.AreEqual(buttons[0].Bounds.Left, strip.Left, 0.001f);
        Assert.AreEqual(buttons[^1].Bounds.Right, strip.Right, 0.001f);
        Assert.AreEqual(0f, strip.Top, 0.001f);
        Assert.AreEqual(StripHeight, strip.Bottom, 0.001f);
        Assert.AreEqual(ButtonWidth * buttons.Count, strip.Width, 0.001f);
    }

    [TestMethod]
    public void NoRoomMeansNoButtons()
    {
        foreach (var (width, height) in new[] { (0f, StripHeight), (ClientWidth, 0f), (-10f, StripHeight) })
        {
            var (strip, buttons) = Win32CaptionButtons.Layout(width, height, maximized: false);

            Assert.AreEqual(0, buttons.Count, $"{width} x {height} has no strip");
            Assert.AreEqual(SKRect.Empty, strip);
        }
    }

    [TestMethod]
    public void HitTestFindsTheButtonUnderThePoint()
    {
        var (_, buttons) = Win32CaptionButtons.Layout(ClientWidth, StripHeight, maximized: false);

        for (var i = 0; i < buttons.Count; i++)
        {
            var bounds = buttons[i].Bounds;
            Assert.AreEqual(i, Win32CaptionButtons.HitTest(buttons, new SKPoint(bounds.MidX, bounds.MidY)),
                $"{buttons[i].Kind} at its centre");
        }
    }

    [TestMethod]
    public void HitTestIgnoresPointsOutsideTheStrip()
    {
        var (strip, buttons) = Win32CaptionButtons.Layout(ClientWidth, StripHeight, maximized: false);

        Assert.AreEqual(-1, Win32CaptionButtons.HitTest(buttons, new SKPoint(strip.Left - 1f, StripHeight / 2f)),
            "just left of the strip belongs to the application");
        Assert.AreEqual(-1, Win32CaptionButtons.HitTest(buttons, new SKPoint(strip.MidX, StripHeight + 1f)),
            "just below the strip belongs to the application");
        Assert.AreEqual(-1, Win32CaptionButtons.HitTest(buttons, new SKPoint(ClientWidth + 1f, StripHeight / 2f)),
            "past the right edge belongs to nobody");
    }

    [TestMethod]
    public void HitTestOnTheStripLeftEdgeIsTheFirstButton()
    {
        var (strip, buttons) = Win32CaptionButtons.Layout(ClientWidth, StripHeight, maximized: false);

        Assert.AreEqual(0, Win32CaptionButtons.HitTest(buttons, new SKPoint(strip.Left, StripHeight / 2f)),
            "the strip has no gap, so its left edge is already the first button");

        var close = buttons[^1].Bounds;
        Assert.AreEqual(buttons.Count - 1,
            Win32CaptionButtons.HitTest(buttons, new SKPoint(close.Right - 1f, close.MidY)),
            "the last pixel before the edge is still close");
    }
}

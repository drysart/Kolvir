using Kolvir;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace Kolvir.Backend.Win32.Tests;

/// <summary>
/// The colour a window erases to before a frame is ready. A resize exposes a strip of client area,
/// and the choice here is what that strip looks like: the window's own background instead of the
/// class brush's black, unless a control that covers the window paints something else.
/// </summary>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32EraseColorTests
{
    private static readonly SKColor Clear = new(0xFFF4F6F9);
    private static readonly SKColor Root = new(0xFF112233);
    private static readonly SKColor Page = new(0xFF445566);
    private static readonly SKColor Card = new(0xFF778899);

    private static readonly SKSize Client = new(800f, 600f);

    private static Panel Sized(Background background, float width = 800f, float height = 600f)
    {
        var control = new Panel
        {
            Background = background,
            Bounds = new SKRect(0f, 0f, width, height),
        };

        return control;
    }

    [TestMethod]
    public void TheHostClearColourIsUsedWhenNothingCoversTheWindow()
    {
        var color = Win32EraseColor.Resolve(root: null, Client, Clear);

        Assert.AreEqual(Clear, color, "a window with no tree still erases to its own background");
    }

    [TestMethod]
    public void ARootThatPaintsTheWholeWindowIsWhatTheEdgeLooksLike()
    {
        var root = Sized(Background.FromColor(Root));

        var color = Win32EraseColor.Resolve(root, Client, Clear);

        Assert.AreEqual(Root, color, "the root hides the clear colour entirely");
    }

    [TestMethod]
    public void ARootThatPaintsNothingFallsBackToTheHostClearColour()
    {
        var root = Sized(Background.None);

        var color = Win32EraseColor.Resolve(root, Client, Clear);

        Assert.AreEqual(Clear, color, "the class background is what shows through a root that paints nothing");
    }

    [TestMethod]
    public void TheInnermostFullWindowControlWins()
    {
        var root = Sized(Background.FromColor(Root));
        var page = Sized(Background.FromColor(Page));

        root.Controls.Add(page);

        var color = Win32EraseColor.Resolve(root, Client, Clear);

        Assert.AreEqual(Page, color, "a page covering the window is in front of the root");
    }

    [TestMethod]
    public void AControlThatDoesNotCoverTheWindowIsIgnored()
    {
        var root = Sized(Background.FromColor(Root));
        var card = Sized(Background.FromColor(Card), width: 200f, height: 100f);

        root.Controls.Add(card);

        var color = Win32EraseColor.Resolve(root, Client, Clear);

        Assert.AreEqual(Root, color, "a card in the corner does not decide the colour of the edge");
    }

    [TestMethod]
    public void AnInsetPageLeavesTheClearColourAtTheEdge()
    {
        var root = Sized(Background.None);
        var page = Sized(Background.FromColor(Page), width: 780f, height: 580f);
        page.Bounds = new SKRect(10f, 10f, 790f, 590f);

        root.Controls.Add(page);

        var color = Win32EraseColor.Resolve(root, Client, Clear);

        Assert.AreEqual(Clear, color, "the margin a page leaves is clear colour, so the edge is too");
    }

    [TestMethod]
    public void AnInvisiblePageIsNotPaintedAndSoNotErasedTo()
    {
        var root = Sized(Background.FromColor(Root));
        var page = Sized(Background.FromColor(Page));
        page.Visible = false;

        root.Controls.Add(page);

        var color = Win32EraseColor.Resolve(root, Client, Clear);

        Assert.AreEqual(Root, color, "a hidden control contributes nothing to what is on screen");
    }

    [TestMethod]
    public void AnUnlaidOutWindowErasesToItsClearColour()
    {
        var root = Sized(Background.FromColor(Root), width: 0f, height: 0f);

        var color = Win32EraseColor.Resolve(root, new SKSize(0f, 0f), Clear);

        Assert.AreEqual(Clear, color, "nothing has bounds yet, so nothing can be claiming the edge");
    }

    [TestMethod]
    public void AFractionalPixelOfLayoutRoundingStillCountsAsCovering()
    {
        var root = Sized(Background.FromColor(Root), width: 799.6f, height: 599.6f);

        var color = Win32EraseColor.Resolve(root, Client, Clear);

        Assert.AreEqual(Root, color, "layout rounds, and a root that fills the window still fills it");
    }
}

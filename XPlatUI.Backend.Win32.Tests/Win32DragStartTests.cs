using Microsoft.VisualStudio.TestTools.UnitTesting;
using XPlatUI;

namespace XPlatUI.Backend.Win32.Tests;

/// <summary>
/// The mapping from a seam resize edge to the hit-test code the system's move loop takes.
/// </summary>
/// <remarks>
/// <see cref="Win32WindowBackend.HitCodeFor"/> is the whole decision behind
/// <see cref="IXPlatWindowBackend.TryBeginResize"/>: once an edge has a code, the drag itself is the
/// platform's own non-client resize loop, so a wrong code is the only way this can misbehave. The
/// pairs that name a corner are the ones worth pinning - <c>Left | Right</c> and <c>Top | Bottom</c>
/// are not corners and must not silently become one.
/// </remarks>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32DragStartTests
{
    [TestMethod]
    public void EachSingleEdgeMapsToItsOwnCode()
    {
        Assert.AreEqual(NativeMethods.HtLeft, Win32WindowBackend.HitCodeFor(XPlatResizeEdge.Left));
        Assert.AreEqual(NativeMethods.HtRight, Win32WindowBackend.HitCodeFor(XPlatResizeEdge.Right));
        Assert.AreEqual(NativeMethods.HtTop, Win32WindowBackend.HitCodeFor(XPlatResizeEdge.Top));
        Assert.AreEqual(NativeMethods.HtBottom, Win32WindowBackend.HitCodeFor(XPlatResizeEdge.Bottom));
    }

    [TestMethod]
    public void TwoAdjacentEdgesMapToTheCornerBetweenThem()
    {
        Assert.AreEqual(NativeMethods.HtTopLeft,
            Win32WindowBackend.HitCodeFor(XPlatResizeEdge.Left | XPlatResizeEdge.Top));
        Assert.AreEqual(NativeMethods.HtTopRight,
            Win32WindowBackend.HitCodeFor(XPlatResizeEdge.Top | XPlatResizeEdge.Right));
        Assert.AreEqual(NativeMethods.HtBottomLeft,
            Win32WindowBackend.HitCodeFor(XPlatResizeEdge.Bottom | XPlatResizeEdge.Left));
        Assert.AreEqual(NativeMethods.HtBottomRight,
            Win32WindowBackend.HitCodeFor(XPlatResizeEdge.Right | XPlatResizeEdge.Bottom));
    }

    [TestMethod]
    public void OppositeEdgesAreNotACornerAndResizeNothing()
    {
        Assert.AreEqual(0, Win32WindowBackend.HitCodeFor(XPlatResizeEdge.Left | XPlatResizeEdge.Right),
            "left and right together are two edges across the window, not a band");
        Assert.AreEqual(0, Win32WindowBackend.HitCodeFor(XPlatResizeEdge.Top | XPlatResizeEdge.Bottom));
    }

    [TestMethod]
    public void NoEdgeAtAllResizesNothing()
    {
        Assert.AreEqual(0, Win32WindowBackend.HitCodeFor(XPlatResizeEdge.None));
        Assert.AreEqual(0, Win32WindowBackend.HitCodeFor(XPlatResizeEdge.Left | XPlatResizeEdge.Right | XPlatResizeEdge.Top));
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using XPlatUI;

namespace XPlatUI.Backend.Win32.Tests;

/// <summary>
/// The two decisions behind the DWM window colours that can be made without a window: which byte
/// order a colour reaches the API in, and which Windows builds implement the attribute at all.
/// </summary>
/// <remarks>
/// Both are silent failures if wrong. A colour in SkiaSharp's order is red where it should be blue,
/// and nothing throws; an attribute set on a build that does not know it is rejected with
/// <c>E_INVALIDARG</c> and the window simply keeps its own colours. The capability is the only place
/// either can be reported honestly, so the build boundary is pinned here rather than assumed.
/// </remarks>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32WindowColorTests
{
    [TestMethod]
    public void ColorsReachDwmInColorRefOrder()
    {
        Assert.AreEqual(0x00332211u, Win32WindowBackend.ToColorRef(new SKColor(0x11, 0x22, 0x33)),
            "COLORREF is 0x00BBGGRR");
        Assert.AreEqual(0x00FF0000u, Win32WindowBackend.ToColorRef(new SKColor(0x00, 0x00, 0xFF)),
            "blue lands in the high byte");
        Assert.AreEqual(0x000000FFu, Win32WindowBackend.ToColorRef(new SKColor(0xFF, 0x00, 0x00)),
            "red lands in the low byte");
    }

    [TestMethod]
    public void AlphaIsDropped()
    {
        Assert.AreEqual(Win32WindowBackend.ToColorRef(new SKColor(0x11, 0x22, 0x33)),
            Win32WindowBackend.ToColorRef(new SKColor(0x11, 0x22, 0x33, 0x00)),
            "COLORREF has no alpha channel");
        Assert.AreEqual(Win32WindowBackend.ToColorRef(new SKColor(0x11, 0x22, 0x33)),
            Win32WindowBackend.ToColorRef(new SKColor(0x11, 0x22, 0x33, 0xFF)));
    }

    [TestMethod]
    public void BothEndsOfTheRangeSurvive()
    {
        Assert.AreEqual(0x00000000u, Win32WindowBackend.ToColorRef(SKColors.Black));
        Assert.AreEqual(0x00FFFFFFu, Win32WindowBackend.ToColorRef(SKColors.White));
    }

    [TestMethod]
    public void WindowColorsNeedWindowsEleven()
    {
        Assert.IsFalse(NativeMethods.SupportsWindowColors(19045), "Windows 10 rejects the attribute");
        Assert.IsFalse(NativeMethods.SupportsWindowColors(21999), "the build before the first one that has it");
        Assert.IsTrue(NativeMethods.SupportsWindowColors(22000), "Windows 11 RTM");
        Assert.IsTrue(NativeMethods.SupportsWindowColors(26100), "Windows 11 24H2");
    }
}

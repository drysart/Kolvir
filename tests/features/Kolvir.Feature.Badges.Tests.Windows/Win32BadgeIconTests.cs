using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace Kolvir.Badges.Tests.Windows;

/// <summary>
/// What the Windows badge looks like, pixel by pixel: the disc, the text inside it, the dot, and the
/// size the shell asks for.
/// </summary>
/// <remarks>
/// The overlay icon is the one thing the feature draws itself, and it is drawn as small as 16 pixels,
/// where a two-pixel error in the geometry turns a numeral into a smear. There is nothing to read in a
/// screenshot at that size either, so the geometry is pinned here rather than eyeballed.
/// </remarks>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32BadgeIconTests
{
    private const int Size = 32;

    [TestMethod]
    public void CountsCapAtNinetyNinePlus()
    {
        Assert.AreEqual("3", Win32BadgeIcon.CountText(3));
        Assert.AreEqual("10", Win32BadgeIcon.CountText(10));
        Assert.AreEqual("99", Win32BadgeIcon.CountText(99), "two digits still fit");
        Assert.AreEqual("99+", Win32BadgeIcon.CountText(100), "a third digit does not");
        Assert.AreEqual("99+", Win32BadgeIcon.CountText(1200), "and a cap is not a truncation: 12+ would say twelve");
        Assert.AreEqual(string.Empty, Win32BadgeIcon.CountText(0), "no unread items draws no number");
    }

    [TestMethod]
    public void TheBadgeIsADiscThatFillsTheIconAndLeavesTheCornersClear()
    {
        using var bitmap = Win32BadgeIcon.Draw(Count(3), Size);

        // Inside the disc, well above the numeral: red.
        var inside = bitmap.GetPixel(Size / 2, 5);
        Assert.AreEqual(255, inside.Alpha, "the disc is opaque");
        Assert.IsTrue(inside.Red > 150 && inside.Green < 110, $"expected the attention red, got {inside}");

        // Outside it: nothing, so the shell's taskbar shows through.
        Assert.AreEqual(0, bitmap.GetPixel(1, 1).Alpha, "the top-left corner is clear");
        Assert.AreEqual(0, bitmap.GetPixel(Size - 2, Size - 2).Alpha, "and so is the bottom-right");
    }

    [TestMethod]
    public void TheNumberIsDrawnInWhiteInsideTheDisc()
    {
        using var bitmap = Win32BadgeIcon.Draw(Count(3), Size);

        Assert.IsTrue(Lit(bitmap, c => c.Red > 200 && c.Green > 200 && c.Blue > 200) > 8,
            "the numeral is white, which nothing else in the icon is");
    }

    [TestMethod]
    public void ADotCoversLessThanACountBadge()
    {
        using var count = Win32BadgeIcon.Draw(Count(3), Size);
        using var dot = Win32BadgeIcon.Draw(Dot(), Size);

        var counted = Lit(count, c => c.Alpha > 0);
        var dotted = Lit(dot, c => c.Alpha > 0);

        Assert.IsTrue(dotted > 0, "the dot is drawn");
        Assert.IsTrue(dotted < counted, $"a dot is smaller than a badge: {dotted} vs {counted}");
        Assert.IsTrue(dotted > counted / 4, $"but not a speck: {dotted} vs {counted}");
    }

    [TestMethod]
    public void ALabelIsDrawnWithoutRunningPastTheIcon()
    {
        using var bitmap = Win32BadgeIcon.Draw(Text("99+"), Size);

        Assert.IsTrue(Lit(bitmap, c => c.Red > 200 && c.Green > 200 && c.Blue > 200) > 8, "the label is white");
        Assert.AreEqual(0, bitmap.GetPixel(0, 0).Alpha, "and the corner is still clear");
    }

    [TestMethod]
    public void AnEmptyBadgeDrawsNothing()
    {
        using var bitmap = Win32BadgeIcon.Draw(BadgeState.Empty, Size);

        Assert.AreEqual(0, Lit(bitmap, c => c.Alpha > 0), "nothing to show means no pixels");
    }

    [TestMethod]
    public void TheIconIsDrawnAtTheSizeTheShellAsksFor()
    {
        foreach (var size in new[] { 16, 20, 24, 32 })
        {
            using var bitmap = Win32BadgeIcon.Draw(Count(7), size);

            Assert.AreEqual(size, bitmap.Width);
            Assert.AreEqual(size, bitmap.Height);
            Assert.IsTrue(Lit(bitmap, c => c.Alpha > 0) > size, $"a {size}-pixel badge is still a badge");
        }
    }

    [TestMethod]
    public void NoIconHandleIsMadeForAnEmptyBadge()
    {
        Assert.AreEqual(IntPtr.Zero, Win32BadgeIcon.Create(BadgeState.Empty, Size),
            "clearing the badge is the shell being told there is no icon, not an empty one");
    }

    [TestMethod]
    public void AnIconHandleIsMadeForANumber()
    {
        var handle = Win32BadgeIcon.Create(Count(3), Size);

        try
        {
            Assert.AreNotEqual(IntPtr.Zero, handle, "the drawn pixels become an icon the shell can take");
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                Win32BadgeNativeMethods.DestroyIcon(handle);
            }
        }
    }

    [TestMethod]
    public void AnIconHandleIsMadeAtEverySizeTheShellUses()
    {
        foreach (var size in new[] { 16, 20, 24, 32 })
        {
            var handle = Win32BadgeIcon.Create(Count(1), size);
            Assert.AreNotEqual(IntPtr.Zero, handle, $"a {size}-pixel icon is built");

            if (handle != IntPtr.Zero)
            {
                Win32BadgeNativeMethods.DestroyIcon(handle);
            }
        }
    }

    private static BadgeState Count(int count) => new(BadgeKind.Count, count, null, false);

    private static BadgeState Text(string text) => new(BadgeKind.Text, 0, text, false);

    private static BadgeState Dot() => new(BadgeKind.Dot, 0, null, false);

    private static int Lit(SKBitmap bitmap, Func<SKColor, bool> matches)
    {
        var lit = 0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (matches(bitmap.GetPixel(x, y)))
                {
                    lit++;
                }
            }
        }

        return lit;
    }
}

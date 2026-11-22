using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The host's clear colour: the union it now accepts, the theme background an unassigned one
/// follows, and the empty window it paints.
/// </summary>
[TestClass]
[TestCategory("Pixels")]
public sealed class UiHostColorTests : HeadlessTestBase
{
    [TestMethod]
    public void TheClearColorReportsTheAssignedCaseAndClearsTheWindow()
    {
        using var scene = HeadlessScene.Create(null, 40f, 30f);
        var host = scene.Host;

        Assert.IsTrue(host.ClearColor.TryGetValue<InheritedValue>(out _), "an unassigned clear colour reads back as inherit");
        Assert.AreEqual(Theme.Background, host.EffectiveClearColor, "and follows the theme background");
        Assert.AreEqual(Theme.Background, scene.PixelAt(20, 15), "the empty window is cleared with it");

        host.ClearColor = new SKColor(0x12, 0x34, 0x56);
        Assert.IsTrue(host.ClearColor.TryGetValue<SKColor>(out var literal));
        Assert.AreEqual(new SKColor(0x12, 0x34, 0x56), literal, "the property reports the colour it was given");
        Assert.AreEqual(new SKColor(0x12, 0x34, 0x56), scene.PixelAt(20, 15), "an assignment clears the window with it");

        host.ClearColor = ThemeColor.Secondary;
        Assert.AreEqual(Theme.Secondary, host.EffectiveClearColor, "a role is read from the current palette");
        Assert.AreEqual(Theme.Secondary, scene.PixelAt(20, 15), "and clears the window with it");

        Theme.Palette = Palette.Dark;

        Assert.AreEqual(Theme.Secondary, host.EffectiveClearColor, "the role follows a palette swap");
        Assert.AreEqual(Theme.Secondary, scene.PixelAt(20, 15), "and so does the next frame");

        host.ClearColor = PropertyValue.Inherited;
        Assert.AreEqual(Theme.Background, scene.PixelAt(20, 15), "and inherit goes back to the theme background");
    }

    [TestMethod]
    public void AnUnspecifiedClearColorIsRefused()
    {
        using var scene = HeadlessScene.Create(null, 40f, 30f);
        scene.Host.ClearColor = default;

        Assert.ThrowsExactly<InvalidOperationException>(
            () => _ = scene.Host.EffectiveClearColor,
            "a default-initialised union holds no case and must not be read as inherit");
    }
}

using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The caret's colour: the union it now accepts, the theme role an unassigned one follows, and how
/// both answer a palette swap.
/// </summary>
[TestClass]
[TestCategory("Core")]
public sealed class CaretTests : HeadlessTestBase
{
    [TestMethod]
    public void TheCaretColorReportsTheAssignedCase()
    {
        using var box = new TextBox();
        var caret = box.Caret;

        Assert.IsTrue(caret.Color.TryGetValue<InheritedValue>(out _), "an unassigned caret reads back as inherit");

        caret.Color = new SKColor(0x12, 0x34, 0x56);
        Assert.IsTrue(caret.Color.TryGetValue<SKColor>(out var literal));
        Assert.AreEqual(new SKColor(0x12, 0x34, 0x56), literal, "the property reports the colour it was given");

        caret.Color = ThemeColor.Caret;
        Assert.IsTrue(caret.Color.TryGetValue<ThemeColor>(out var role));
        Assert.AreEqual(ThemeColor.Caret, role, "and the role as well");

        caret.Color = PropertyValue.Inherited;
        Assert.IsTrue(caret.Color.TryGetValue<InheritedValue>(out _), "and inherit can be assigned back");
    }

    [TestMethod]
    public void TheUnassignedCaretFollowsTheThemeAndARoleFollowsAPaletteSwap()
    {
        using var box = new TextBox();
        var caret = box.Caret;

        Assert.AreEqual(Theme.Caret, caret.EffectiveColor, "nothing assigned follows the theme's caret colour");

        caret.Color = ThemeColor.Accent;
        Assert.AreEqual(Theme.Accent, caret.EffectiveColor, "a role is read from the current palette");

        Theme.Palette = Palette.Dark;

        Assert.AreEqual(Theme.Accent, caret.EffectiveColor, "and follows a palette swap");
    }

    [TestMethod]
    public void AnUnspecifiedCaretColorIsRefused()
    {
        using var box = new TextBox();
        var caret = box.Caret;
#pragma warning disable KOLVIR0002 // This test is about the value the rule exists to refuse.
        caret.Color = default;
#pragma warning restore KOLVIR0002

        Assert.ThrowsExactly<InvalidOperationException>(
            () => _ = caret.EffectiveColor,
            "a default-initialised union holds no case and must not be read as inherit");
    }
}

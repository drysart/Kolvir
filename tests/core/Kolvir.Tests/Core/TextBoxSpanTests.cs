using Kolvir;
using Kolvir.Spans;
using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// The annotated spans a text box draws: the colour a squiggle resolves to, and where that colour
/// comes from when the span does not name one.
/// </summary>
/// <remarks>
/// A span is caller-owned and holds no field reference, so the resolved colour is asked for with
/// the field that is drawing it. An unassigned span takes that field's text colour; the
/// conventional spelling red is <see cref="TextBoxSpan.DefaultColor"/> and has to be assigned.
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class TextBoxSpanTests : HeadlessTestBase
{
    [TestMethod]
    public void ASpanWithNoColourTakesTheFieldsForeColor()
    {
        using var field = new TextBox();
        var color = new SKColor(0x12, 0x34, 0x56);
        field.ForeColor = color;
        var span = new TextBoxSpan(0, 3);

        Assert.IsTrue(span.Color.TryGetValue<InheritedValue>(out _), "an unassigned colour reads back as inherit");
        Assert.AreEqual(color, span.EffectiveColor(field), "and resolves to the field's effective text colour");
    }

    [TestMethod]
    public void ASpanColorCanBeALiteralOrARole()
    {
        using var field = new TextBox();
        field.ForeColor = new SKColor(0xAA, 0xBB, 0xCC);
        var span = new TextBoxSpan(0, 3);

        span.Color = TextBoxSpan.DefaultColor;
        Assert.AreEqual(TextBoxSpan.DefaultColor, span.EffectiveColor(field), "a literal is used as it is");

        span.Color = ThemeColor.Accent;
        Assert.AreEqual(
            Theme.ColorOf(ThemeColor.Accent),
            span.EffectiveColor(field),
            "a role is resolved when it is read and beats the field's own colour");

        span.Color = InheritedValue.Instance;
        Assert.AreEqual(field.EffectiveForeColor, span.EffectiveColor(field), "and assigning inherit goes back to the field");
    }

    [TestMethod]
    public void AnUnspecifiedSpanColorIsRefused()
    {
        using var field = new TextBox();
        var span = new TextBoxSpan(0, 1) { Color = default };

        Assert.ThrowsExactly<InvalidOperationException>(
            () => _ = span.EffectiveColor(field),
            "a default-initialised union holds no case and must not be read as inherit");
    }

    [TestMethod]
    public void ASpanCannotBeResolvedWithoutItsField()
    {
        var span = new TextBoxSpan(0, 1);

        Assert.ThrowsExactly<ArgumentNullException>(() => _ = span.EffectiveColor(null!));
    }
}

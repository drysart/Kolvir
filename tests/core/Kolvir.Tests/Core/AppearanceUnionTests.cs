using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// The union-typed appearance properties on <see cref="Control"/>: what the property holds, what
/// <c>Effective*</c> resolves it to, and how the inherited answer is cached and dropped.
/// </summary>
/// <remarks>
/// The property reports the assignment, so an unassigned one reads back as
/// <see cref="InheritedValue"/>. The resolution is the control's job: the nearest ancestor that
/// assigned a value answers, the class default answers when nothing did, and a default-initialised
/// union is refused where it is read rather than treated as inherit.
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class AppearanceUnionTests : HeadlessTestBase
{
    private static readonly SKSize Unbounded = new(float.PositiveInfinity, float.PositiveInfinity);

    [TestMethod]
    public void TheFontPropertyReportsTheAssignedCase()
    {
        using var font = TestFont.Create(14f);
        var label = new Label("x");

        Assert.IsTrue(label.Font.TryGetValue<InheritedValue>(out _), "an unassigned font reads back as inherit");

        label.Font = font;
        Assert.IsTrue(label.Font.TryGetValue<Font>(out var assigned));
        Assert.AreSame(font, assigned, "the property reports the font it was given");
        Assert.IsFalse(label.Font.TryGetValue<InheritedValue>(out _));

        label.Font = InheritedValue.Instance;
        Assert.IsTrue(label.Font.TryGetValue<InheritedValue>(out _), "and inherit can be assigned back");
        Assert.IsFalse(label.Font.TryGetValue<Font>(out _));
    }

    [TestMethod]
    public void AnUnspecifiedFontIsRefused()
    {
        var label = new Label("x");
        label.Font = default;

        Assert.ThrowsExactly<InvalidOperationException>(
            () => _ = label.EffectiveFont,
            "a default-initialised union holds no case and must not be read as inherit");
    }

    [TestMethod]
    public void InheritingAgainRestoresTheParentFontAndLaysOutAgain()
    {
        using var parentFont = TestFont.Create(20f);
        using var ownFont = TestFont.Create(9f);

        var panel = new Panel { Font = parentFont };
        var label = new Label("inherited") { AutoSize = true };
        panel.Controls.Add(label);

        label.Measure(Unbounded);
        float inheritedWidth = label.DesiredSize.Width;

        Assert.AreSame(parentFont, label.EffectiveFont, "an unparented assignment answers its children");

        label.Font = ownFont;
        label.Measure(Unbounded);

        Assert.AreSame(ownFont, label.EffectiveFont);
        Assert.IsTrue(label.DesiredSize.Width < inheritedWidth, "the smaller font should measure narrower");

        label.Font = InheritedValue.Instance;
        label.Measure(Unbounded);

        Assert.AreSame(parentFont, label.EffectiveFont, "inheriting again should use the parent's font");
        Assert.AreEqual(inheritedWidth, label.DesiredSize.Width, 0.01f, "and re-measure at that font");
    }

    [TestMethod]
    public void AFontAssignedAboveAChildThatAlreadyResolvedIsPickedUp()
    {
        using var outerFont = TestFont.Create(18f);
        using var innerFont = TestFont.Create(11f);

        var outer = new Panel();
        var middle = new Panel();
        var label = new Label("x");
        middle.Controls.Add(label);
        outer.Controls.Add(middle);

        Assert.AreSame(Theme.DefaultFont, label.EffectiveFont, "nothing is assigned yet");

        middle.Font = innerFont;
        Assert.AreSame(innerFont, label.EffectiveFont, "the nearest ancestor answers");

        outer.Font = outerFont;
        Assert.AreSame(innerFont, label.EffectiveFont, "the nearer assignment still wins");

        middle.Font = InheritedValue.Instance;
        Assert.AreSame(outerFont, label.EffectiveFont, "dropping it falls through to the next ancestor");
    }

    [TestMethod]
    public void ReparentingResolvesAgainstTheNewParent()
    {
        using var firstFont = TestFont.Create(15f);
        using var secondFont = TestFont.Create(25f);

        var first = new Panel { Font = firstFont };
        var second = new Panel { Font = secondFont };
        var label = new Label("x");

        first.Controls.Add(label);
        Assert.AreSame(firstFont, label.EffectiveFont);

        first.Controls.Remove(label);
        second.Controls.Add(label);

        Assert.AreSame(secondFont, label.EffectiveFont, "a moved control resolves against where it is now");
    }

    [TestMethod]
    public void AnUnparentedControlUsesItsClassDefaultFont()
    {
        var label = new Label("x");
        var button = new Button("y");

        Assert.AreSame(Theme.DefaultFont, label.EffectiveFont, "a label falls back to the theme's font");
        Assert.AreSame(Theme.EmphasizedFont, button.EffectiveFont, "a button falls back to its own class default");
    }
}

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

        label.Font = PropertyValue.Inherited;
        Assert.IsTrue(label.Font.TryGetValue<InheritedValue>(out _), "and inherit can be assigned back");
        Assert.IsFalse(label.Font.TryGetValue<Font>(out _));
    }

    [TestMethod]
    public void AnUnspecifiedFontIsRefused()
    {
        var label = new Label("x");
#pragma warning disable KOLVIR0002 // This test is about the value the rule exists to refuse.
        label.Font = default;
#pragma warning restore KOLVIR0002

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

        label.Font = PropertyValue.Inherited;
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

        middle.Font = PropertyValue.Inherited;
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

    [TestMethod]
    public void TheForeColorPropertyReportsTheAssignedCase()
    {
        var label = new Label("x");
        var color = new SKColor(0xFF2F6F4F);

        Assert.IsTrue(label.ForeColor.TryGetValue<InheritedValue>(out _), "an unassigned colour reads back as inherit");

        label.ForeColor = color;
        Assert.IsTrue(label.ForeColor.TryGetValue<SKColor>(out var literal));
        Assert.AreEqual(color, literal);
        Assert.AreEqual(color, label.EffectiveForeColor);

        label.ForeColor = ThemeColor.Accent;
        Assert.IsTrue(label.ForeColor.TryGetValue<ThemeColor>(out var role));
        Assert.AreEqual(ThemeColor.Accent, role);
        Assert.AreEqual(Theme.Accent, label.EffectiveForeColor);

        label.ForeColor = PropertyValue.Inherited;
        Assert.IsTrue(label.ForeColor.TryGetValue<InheritedValue>(out _), "and inherit can be assigned back");
    }

    [TestMethod]
    public void AnUnspecifiedForeColorIsRefused()
    {
        var label = new Label("x");
#pragma warning disable KOLVIR0002 // This test is about the value the rule exists to refuse.
        label.ForeColor = default;
#pragma warning restore KOLVIR0002

        Assert.ThrowsExactly<InvalidOperationException>(
            () => _ = label.EffectiveForeColor,
            "a default-initialised union holds no case and must not be read as inherit");
    }

    [TestMethod]
    public void AnUnparentedControlUsesTheThemesForeground()
    {
        var label = new Label("x");

        Assert.AreEqual(Theme.Foreground, label.EffectiveForeColor, "transparent text would be invisible, so the neutral default is used");
    }

    [TestMethod]
    public void ARoleAssignedToAContainerAnswersForItsDescendants()
    {
        var panel = new Panel { ForeColor = ThemeColor.MutedForeground };
        var label = new Label("x");
        panel.Controls.Add(label);

        Assert.AreEqual(Theme.MutedForeground, panel.EffectiveForeColor);
        Assert.AreEqual(Theme.MutedForeground, label.EffectiveForeColor, "a role is a value and inherits like a literal");
    }

    [TestMethod]
    public void ARoleIsResolvedWhenItIsReadSoAPaletteSwapRecolours()
    {
        var panel = new Panel { ForeColor = ThemeColor.Accent };
        var label = new Label("x");
        panel.Controls.Add(label);

        var light = label.EffectiveForeColor;
        Assert.AreEqual(Theme.Accent, light, "the role is read from the current palette");
        Assert.IsTrue(panel.ForeColor.TryGetValue<ThemeColor>(out _), "the property still reports the role");

        Theme.Palette = Palette.Dark;

        Assert.AreEqual(Theme.Accent, label.EffectiveForeColor, "the same tree follows the new palette");
        Assert.AreNotEqual(light, label.EffectiveForeColor, "the two palettes should differ");
    }

    [TestMethod]
    public void AForeColorAssignedAboveAChildThatAlreadyResolvedIsPickedUp()
    {
        var outer = new Panel();
        var label = new Label("x");
        outer.Controls.Add(label);

        Assert.AreEqual(Theme.Foreground, label.EffectiveForeColor, "nothing is assigned yet");

        outer.ForeColor = new SKColor(0xFF203040);

        Assert.AreEqual(new SKColor(0xFF203040), label.EffectiveForeColor, "the cached answer is dropped");
    }

    [TestMethod]
    public void AGroupBoxKeepsItsMutedCaptionWhileItsChildrenUseTheForeground()
    {
        var group = new GroupBox("caption");
        var label = new Label("x");
        group.Controls.Add(label);

        Assert.AreEqual(Theme.MutedForeground, group.EffectiveForeColor, "the caption uses the class default role");
        Assert.AreEqual(Theme.Foreground, label.EffectiveForeColor, "the class default is not inherited");

        group.ForeColor = new SKColor(0xFF101010);

        Assert.AreEqual(new SKColor(0xFF101010), label.EffectiveForeColor, "an assigned colour does answer for the children");
    }

    [TestMethod]
    public void TheBackColorPropertyReportsTheAssignedCase()
    {
        var panel = new Panel();
        var color = new SKColor(0xFF2F6F4F);

        Assert.IsTrue(panel.BackColor.TryGetValue<InheritedValue>(out _), "an unassigned background reads back as inherit");

        panel.BackColor = color;
        Assert.IsTrue(panel.BackColor.TryGetValue<SKColor>(out var literal));
        Assert.AreEqual(color, literal);
        Assert.AreEqual(color, panel.EffectiveBackColor);

        panel.BackColor = ThemeColor.Surface;
        Assert.IsTrue(panel.BackColor.TryGetValue<ThemeColor>(out var role));
        Assert.AreEqual(ThemeColor.Surface, role);
        Assert.AreEqual(Theme.Surface, panel.EffectiveBackColor);

        panel.BackColor = PropertyValue.Inherited;
        Assert.IsTrue(panel.BackColor.TryGetValue<InheritedValue>(out _), "and inherit can be assigned back");
    }

    [TestMethod]
    public void AnUnspecifiedBackColorIsRefused()
    {
        var panel = new Panel();
#pragma warning disable KOLVIR0002 // This test is about the value the rule exists to refuse.
        panel.BackColor = default;
#pragma warning restore KOLVIR0002

        Assert.ThrowsExactly<InvalidOperationException>(
            () => _ = panel.EffectiveBackColor,
            "a default-initialised union holds no case and must not be read as inherit");
    }

    [TestMethod]
    public void AnUnparentedControlPaintsNothingUnlessItsClassPaints()
    {
        var label = new Label("x");
        var panel = new Panel();
        var field = new TextBox();

        Assert.AreEqual(SKColors.Transparent, label.EffectiveBackColor, "a label paints nothing, the least destructive answer");
        Assert.AreEqual(SKColors.Transparent, panel.EffectiveBackColor, "and so does a plain panel");
        Assert.AreEqual(Theme.InputBackground, field.EffectiveBackColor, "a field falls back to its own fill");
    }

    [TestMethod]
    public void ATransparentClassDefaultStopsInheritance()
    {
        var ancestorColour = new SKColor(0xFFE8EEF9);
        var stage = new Panel { BackColor = ancestorColour };
        var label = new Label("paints nothing");
        var field = new TextBox();
        stage.Controls.Add(label);
        stage.Controls.Add(field);

        Assert.AreEqual(SKColors.Transparent, label.EffectiveBackColor, "a label keeps painting nothing inside a filled panel");
        Assert.AreEqual(ancestorColour, field.EffectiveBackColor, "a control that paints takes the ancestor's colour");
    }

    [TestMethod]
    public void AnAssignedTransparentIsInheritedByDescendants()
    {
        var stage = new Panel { BackColor = SKColors.Transparent };
        var field = new TextBox();
        stage.Controls.Add(field);

        Assert.AreEqual(
            SKColors.Transparent,
            field.EffectiveBackColor,
            "an assigned transparent is a colour and answers for descendants, unlike a transparent class default");
    }

    [TestMethod]
    public void ABackgroundRoleIsResolvedWhenItIsRead()
    {
        var stage = new Panel { BackColor = ThemeColor.Surface };
        var field = new TextBox();
        stage.Controls.Add(field);

        var light = field.EffectiveBackColor;
        Assert.AreEqual(Theme.Surface, light);

        Theme.Palette = Palette.Dark;

        Assert.AreEqual(Theme.Surface, field.EffectiveBackColor, "the same tree follows the new palette");
        Assert.AreNotEqual(light, field.EffectiveBackColor, "the two palettes should differ");
    }

    [TestMethod]
    public void InheritingAgainRestoresTheParentBackground()
    {
        var parentColour = new SKColor(0xFF405060);
        var stage = new Panel { BackColor = parentColour };
        var field = new TextBox { BackColor = new SKColor(0xFF101010) };
        stage.Controls.Add(field);

        Assert.AreEqual(new SKColor(0xFF101010), field.EffectiveBackColor);

        field.BackColor = PropertyValue.Inherited;

        Assert.AreEqual(parentColour, field.EffectiveBackColor, "the nearest ancestor answers again");
    }

    [TestMethod]
    public void ABackgroundAssignedAboveAChildThatAlreadyResolvedIsPickedUp()
    {
        var outer = new Panel();
        var field = new TextBox();
        outer.Controls.Add(field);

        Assert.AreEqual(Theme.InputBackground, field.EffectiveBackColor, "nothing is assigned yet");

        outer.BackColor = new SKColor(0xFF203040);

        Assert.AreEqual(new SKColor(0xFF203040), field.EffectiveBackColor, "the cached answer is dropped");
    }

    [TestMethod]
    public void ToolStripItemsInheritFromTheirStrip()
    {
        using var stripFont = TestFont.Create(16f);
        using var itemFont = TestFont.Create(10f);

        var strip = new ToolStrip { Font = stripFont, ForeColor = ThemeColor.Accent };
        var label = new ToolStripLabel("Ready");
        strip.Items.Add(label);

        Assert.AreSame(stripFont, label.EffectiveFont, "an item with no font of its own uses its strip's");
        Assert.AreEqual(Theme.Accent, label.EffectiveForeColor, "and its strip's effective text colour");

        label.Font = itemFont;
        label.ForeColor = new SKColor(0xFF102030);

        Assert.AreSame(itemFont, label.EffectiveFont, "an item's own font wins");
        Assert.AreEqual(new SKColor(0xFF102030), label.EffectiveForeColor);

        label.Font = PropertyValue.Inherited;
        label.ForeColor = PropertyValue.Inherited;

        Assert.AreSame(stripFont, label.EffectiveFont, "inheriting again restores the strip's font");
        Assert.AreEqual(Theme.Accent, label.EffectiveForeColor, "and the strip's colour");
    }

    [TestMethod]
    public void AnUnparentedToolStripItemUsesTheThemesDefaults()
    {
        var label = new ToolStripLabel("x");

        Assert.AreSame(Theme.DefaultFont, label.EffectiveFont);
        Assert.AreEqual(Theme.Foreground, label.EffectiveForeColor);
    }
}

using Kolvir;
using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// The inherited-font override: what a partial font composes to, how it travels down a tree, and
/// why assigning an equal one is a no-op.
/// </summary>
/// <remarks>
/// <para>
/// <c>label.Font = InheritedFont.WithSize(14f)</c> names one member and inherits the rest, which is
/// the one thing a complete <see cref="Font"/> cannot express: a <see cref="Font"/> always names its
/// family, so <c>Theme.DefaultFont.WithSize(14f)</c> stops inheriting the parent chain.
/// </para>
/// <para>
/// The resolution tests read <see cref="Control.EffectiveFont"/>, because that is where composition
/// happens and the override type deliberately has no measuring or drawing surface of its own. The
/// family tests use the bundled face so the widths do not depend on the machine.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class FontOverrideTests : HeadlessTestBase
{
    [TestMethod]
    public void AnOverrideComposesOntoTheInheritedFont()
    {
        var inherited = new Font(TestFont.Family, 12f, 700, italic: true);

        var panel = new Panel { Font = inherited };
        var label = new Label("x") { Font = InheritedFont.WithSize(20f) };
        panel.Controls.Add(label);

        Assert.AreEqual(
            new Font(TestFont.Family, 20f, 700, italic: true),
            label.EffectiveFont,
            "the size is the only member asked for, so the rest comes from the inherited font");
    }

    [TestMethod]
    public void AnOverrideThatNamesNothingResolvesToTheInheritedFontItself()
    {
        var inherited = new Font(TestFont.Family, 12f);

        var panel = new Panel { Font = inherited };
        var label = new Label("x");
        panel.Controls.Add(label);

        Assert.AreSame(inherited, label.EffectiveFont, "nothing assigned inherits the panel's font");

        // Null on every member is the neutral element of the chain, and it is how a chain is narrowed
        // back. It is a different union case from PropertyValue.Inherited - the property reports a
        // FontOverride - but the font it resolves to is the same object.
        label.Font = InheritedFont.WithSize(null);

        Assert.IsTrue(label.Font.TryGetValue<FontOverride>(out var assigned), "the case is the override");
        Assert.IsNull(assigned!.FamilyName);
        Assert.IsNull(assigned.Size);
        Assert.IsNull(assigned.Weight);
        Assert.IsNull(assigned.Italic);
        Assert.AreSame(inherited, label.EffectiveFont, "and the effective font is the inherited one, not a copy");

        label.Font = PropertyValue.Inherited;

        Assert.AreSame(inherited, label.EffectiveFont, "which is the same answer the inherited case gives");
    }

    [TestMethod]
    public void OverridesComposeDownThreeLevels()
    {
        var outer = new Panel { Font = InheritedFont.WithSize(20f) };
        var middle = new Panel { Font = InheritedFont.WithWeight(700) };
        var label = new Label("x") { Font = InheritedFont.WithFamily(TestFont.Family) };
        middle.Controls.Add(label);
        outer.Controls.Add(middle);

        var outermost = outer.EffectiveFont;
        Assert.AreEqual(20f, outermost.Size, 0.01f);
        Assert.AreEqual(Theme.DefaultFont.Weight, outermost.Weight, "an unset weight keeps the class default");
        Assert.AreEqual(Theme.DefaultFont.FamilyName, outermost.FamilyName, "and the family too");

        var middleFont = middle.EffectiveFont;
        Assert.AreEqual(20f, middleFont.Size, 0.01f, "the outer override is this level's base");
        Assert.AreEqual(700, middleFont.Weight, "and this level's override applies on top of it");

        var leaf = label.EffectiveFont;
        Assert.AreEqual(20f, leaf.Size, 0.01f, "the size still comes from the outermost override");
        Assert.AreEqual(700, leaf.Weight, "the weight from the middle one");
        Assert.AreEqual(TestFont.Family, leaf.FamilyName, "and the family from the leaf's own override");
    }

    [TestMethod]
    public void AnOverrideOverTheClassDefaultComposesOntoIt()
    {
        var label = new Label("x") { Font = InheritedFont.WithSize(17f) };
        var button = new Button("y") { Font = InheritedFont.WithItalic(true) };

        Assert.AreEqual(17f, label.EffectiveFont.Size, 0.01f);
        Assert.AreEqual(Theme.DefaultFont.FamilyName, label.EffectiveFont.FamilyName);
        Assert.AreEqual(Theme.DefaultFont.Weight, label.EffectiveFont.Weight);

        Assert.AreEqual(
            Theme.EmphasizedFont.Size,
            button.EffectiveFont.Size,
            0.01f,
            "a class default is not inherited, so an override with nothing above it composes onto its own");
        Assert.AreEqual(Theme.EmphasizedFont.FamilyName, button.EffectiveFont.FamilyName);
        Assert.AreEqual(Theme.EmphasizedFont.Weight, button.EffectiveFont.Weight);
        Assert.IsTrue(button.EffectiveFont.Italic);
    }

    [TestMethod]
    public void AnOverrideFamilyResolvesLikeAnyDescription()
    {
        string family = TestFont.Register();

        var label = new Label("Hello") { Font = InheritedFont.WithFamily(family).WithSize(15f) };

        Assert.AreEqual(family, label.EffectiveFont.FamilyName, "the registered name is the description's name");

        var expected = new Font(family, 15f);
        Assert.AreEqual(
            expected.MeasureWidth("Hello"),
            label.EffectiveFont.MeasureWidth("Hello"),
            0.01f,
            "and it measures like the font the same description builds");
    }

    [TestMethod]
    public void AnUnsetMemberIsNotAnEmptyOrZeroValue()
    {
        var emptyFamily = InheritedFont.WithFamily(string.Empty);
        var zeroSize = InheritedFont.WithSize(0f);
        var nothing = InheritedFont.WithFamily(null).WithSize(null);

        Assert.AreEqual(string.Empty, emptyFamily.FamilyName, "an empty family is a value, not a gap");
        Assert.AreEqual(0f, zeroSize.Size!.Value, "and a zero size is a size");
        Assert.IsNull(nothing.FamilyName);
        Assert.IsNull(nothing.Size);

        var namedDefault = new Label("x") { Font = emptyFamily };
        Assert.AreEqual(
            Theme.DefaultFont.FamilyName,
            namedDefault.EffectiveFont.FamilyName,
            "an empty family means the platform's default family, exactly as it does for Font");

        var atZero = new Label("x") { Font = zeroSize };
        Assert.AreEqual(0f, atZero.EffectiveFont.Size, 0.01f, "a zero size draws at zero");

        var inheriting = new Label("x") { Font = nothing };
        Assert.AreSame(Theme.DefaultFont, inheriting.EffectiveFont, "while unset keeps the inherited value");
    }

    [TestMethod]
    public void AnEqualButNewOverrideAssignsNothing()
    {
        var label = new CountingLabel("x");
        var panel = new Panel { Font = new Font(TestFont.Family, 12f) };
        panel.Controls.Add(label);

        Assert.AreSame(panel.EffectiveFont, label.EffectiveFont, "resolve first, so the walk is cached");

        int before = label.AppearanceChanges;

        label.Font = InheritedFont.WithSize(14f);

        Assert.IsTrue(label.Font.TryGetValue<FontOverride>(out var first), "the override is the assignment");
        int afterFirst = label.AppearanceChanges;
        Assert.IsTrue(afterFirst > before, "assigning a different font changes the appearance");

        label.Font = InheritedFont.WithSize(14f);

        Assert.IsTrue(label.Font.TryGetValue<FontOverride>(out var second), "the second assignment is the same case");
        Assert.AreSame(first, second, "an equal override is not stored, so the first one is still reported");
        Assert.AreEqual(afterFirst, label.AppearanceChanges, "and nothing was invalidated");
    }

    [TestMethod]
    public void AnAncestorFontAssignedLaterRecomposesTheOverride()
    {
        var panel = new Panel();
        var label = new Label("x") { Font = InheritedFont.WithSize(18f) };
        panel.Controls.Add(label);

        Assert.AreEqual(18f, label.EffectiveFont.Size, 0.01f, "the class default is the base at first");
        Assert.AreEqual(Theme.DefaultFont.FamilyName, label.EffectiveFont.FamilyName);

        panel.Font = new Font(TestFont.Family, 9f, 700);

        var recomposed = label.EffectiveFont;
        Assert.AreEqual(18f, recomposed.Size, 0.01f, "the override is still applied");
        Assert.AreEqual(700, recomposed.Weight, "on top of the newly assigned font");
        Assert.AreEqual(TestFont.Family, recomposed.FamilyName);
    }

    [TestMethod]
    public void AnOverrideRemeasuresAndInheritingAgainRestoresTheParentFont()
    {
        var parentFont = new Font(TestFont.Family, 11f);

        var panel = new Panel { Font = parentFont };
        var label = new Label("Inherited font") { AutoSize = true };
        panel.Controls.Add(label);

        var size = new SKSize(float.PositiveInfinity, float.PositiveInfinity);
        label.Measure(size);
        float inheritedWidth = label.DesiredSize.Width;

        label.Font = InheritedFont.WithSize(22f);
        label.Measure(size);

        Assert.IsTrue(label.DesiredSize.Width > inheritedWidth, "the larger override should measure wider");
        Assert.AreEqual(22f, label.EffectiveFont.Size, 0.01f);

        label.Font = PropertyValue.Inherited;
        label.Measure(size);

        Assert.AreSame(parentFont, label.EffectiveFont, "inheriting again restores the parent's font");
        Assert.AreEqual(inheritedWidth, label.DesiredSize.Width, 0.01f);
    }

    [TestMethod]
    public void EffectiveFontStaysTheEscapeHatch()
    {
        var label = new Label("x") { Font = InheritedFont.WithSize(14f) };

        var derived = label.EffectiveFont.WithSize(28f);

        Assert.AreEqual(28f, derived.Size, 0.01f);
        Assert.AreEqual(label.EffectiveFont.FamilyName, derived.FamilyName, "a complete font derives without a parent");
        Assert.IsTrue(derived.MeasureWidth("Hello") > 0f, "and it measures without asking a parent for anything");
    }

    [TestMethod]
    public void ToolStripItemOverridesComposeOntoTheStrip()
    {
        var stripFont = new Font(TestFont.Family, 11f, italic: true);

        var strip = new ToolStrip { Font = stripFont };
        var item = new ToolStripLabel("Ready") { Font = InheritedFont.WithSize(17f) };
        strip.Items.Add(item);

        Assert.AreEqual(
            new Font(TestFont.Family, 17f, italic: true),
            item.EffectiveFont,
            "the item inherits the strip's family and slant and changes only the size");

        Assert.IsTrue(item.Font.TryGetValue<FontOverride>(out var first));

        item.Font = InheritedFont.WithSize(17f);

        Assert.IsTrue(item.Font.TryGetValue<FontOverride>(out var second));
        Assert.AreSame(first, second, "an equal override is a no-op on an item too");

        item.Font = PropertyValue.Inherited;

        Assert.AreSame(stripFont, item.EffectiveFont, "and inheriting again restores the strip's font");
    }

    [TestMethod]
    public void AnOverrideThatSetsOnlyTheFamilyChangesOnlyTheFamily()
    {
        var inherited = new Font(TestFont.Family, 11f, 500, italic: true);

        var composed = Compose(inherited, InheritedFont.WithFamily("Segoe UI"));

        Assert.AreEqual("Segoe UI", composed.FamilyName, "the family is the member the override named");
        Assert.AreEqual(inherited.Size, composed.Size, 0.01f);
        Assert.AreEqual(inherited.Weight, composed.Weight);
        Assert.AreEqual(inherited.Italic, composed.Italic);
    }

    [TestMethod]
    public void AnOverrideThatSetsOnlyTheSizeChangesOnlyTheSize()
    {
        var inherited = new Font(TestFont.Family, 11f, 500, italic: true);

        var composed = Compose(inherited, InheritedFont.WithSize(19f));

        Assert.AreEqual(19f, composed.Size, 0.01f, "the size is the member the override named");
        Assert.AreEqual(inherited.FamilyName, composed.FamilyName);
        Assert.AreEqual(inherited.Weight, composed.Weight);
        Assert.AreEqual(inherited.Italic, composed.Italic);
    }

    [TestMethod]
    public void AnOverrideThatSetsOnlyTheWeightChangesOnlyTheWeight()
    {
        var inherited = new Font(TestFont.Family, 11f, 500, italic: true);

        var composed = Compose(inherited, InheritedFont.WithWeight(300));

        Assert.AreEqual(300, composed.Weight, "the weight is the member the override named");
        Assert.AreEqual(inherited.FamilyName, composed.FamilyName);
        Assert.AreEqual(inherited.Size, composed.Size, 0.01f);
        Assert.AreEqual(inherited.Italic, composed.Italic);
    }

    [TestMethod]
    public void AnOverrideThatSetsOnlyTheItalicChangesOnlyTheItalic()
    {
        var inherited = new Font(TestFont.Family, 11f, 500, italic: false);

        var composed = Compose(inherited, InheritedFont.WithItalic(true));

        Assert.IsTrue(composed.Italic, "the slant is the member the override named");
        Assert.AreEqual(inherited.FamilyName, composed.FamilyName);
        Assert.AreEqual(inherited.Size, composed.Size, 0.01f);
        Assert.AreEqual(inherited.Weight, composed.Weight);
    }

    [TestMethod]
    public void AChainThatSetsEveryMemberTakesEveryMemberFromTheOverride()
    {
        var inherited = new Font("Segoe UI", 9f, 400, italic: false);

        var composed = Compose(
            inherited,
            InheritedFont.WithFamily(TestFont.Family).WithSize(14f).WithWeight(600).WithItalic(true));

        // A new member on Font that the override does not compose would leave one of these inherited,
        // which is the failure the four isolation tests above and this one exist to catch.
        Assert.AreEqual(TestFont.Family, composed.FamilyName);
        Assert.AreEqual(14f, composed.Size, 0.01f);
        Assert.AreEqual(600, composed.Weight);
        Assert.IsTrue(composed.Italic);
        Assert.AreEqual(new Font(TestFont.Family, 14f, 600, italic: true), composed);
    }

    [TestMethod]
    public void AMemberSetTwiceTakesTheLastValue()
    {
        var fontOverride = InheritedFont.WithSize(20f).WithSize(14f);

        Assert.AreEqual(14f, fontOverride.Size!.Value, 0.01f, "the chain keeps the last value");

        var composed = Compose(new Font(TestFont.Family, 9f), fontOverride);

        Assert.AreEqual(14f, composed.Size, 0.01f, "and that is what composes");
    }

    [TestMethod]
    public void OverrideWithLeavesAnUnsetMemberUnsetAndASetMemberSet()
    {
        FontOverride fromUnset = InheritedFont.AsIs.With(size: 14f);

        Assert.IsNull(fromUnset.FamilyName, "a member the receiver did not name stays unset");
        Assert.AreEqual(14f, fromUnset.Size!.Value, 0.01f);
        Assert.IsNull(fromUnset.Weight);
        Assert.IsNull(fromUnset.Italic);

        FontOverride fromSet = InheritedFont.WithFamily(TestFont.Family).WithSize(20f).With(weight: 700);

        Assert.AreEqual(TestFont.Family, fromSet.FamilyName, "a member the receiver named keeps its value");
        Assert.AreEqual(20f, fromSet.Size!.Value, 0.01f, "an omitted size keeps the one the override already names");
        Assert.AreEqual(700, fromSet.Weight!.Value, "and the named weight is added");
        Assert.IsNull(fromSet.Italic, "while the slant stays inherited");
    }

    [TestMethod]
    public void OverrideWithClearsAMemberSuppliedAsNull()
    {
        FontOverride named = InheritedFont.WithSize(20f).WithWeight(700);

        FontOverride cleared = named.With(size: null);

        Assert.IsNull(cleared.Size, "a supplied null clears the size back to inherited");
        Assert.AreEqual(700, cleared.Weight!.Value, "while an omitted member keeps its value");
        Assert.IsNull(named.With(size: PropertyValue.Inherited).Size, "and PropertyValue.Inherited says the same thing more clearly");
        Assert.IsNull(named.WithWeight(null).Weight, "the single-member method clears the same way");
    }

    [TestMethod]
    public void WithFamilyHasAllFourStates()
    {
        FontOverride named = InheritedFont.WithFamily("Segoe UI");

        Assert.AreEqual("Segoe UI", named.With(size: 14f).FamilyName, "an omitted argument leaves it");
        Assert.AreEqual(TestFont.Family, named.With(family: TestFont.Family).FamilyName, "a value sets it");
        Assert.IsNull(named.With(family: PropertyValue.Inherited).FamilyName, "Inherited clears it");
        Assert.IsNull(named.With(family: (string?)null).FamilyName, "an explicit null names a null family");
    }

    [TestMethod]
    public void WithSizeHasAllFourStates()
    {
        FontOverride sized = InheritedFont.WithSize(20f);

        Assert.AreEqual(20f, sized.With(weight: 700).Size!.Value, 0.01f, "an omitted argument leaves it");
        Assert.AreEqual(14f, sized.With(size: 14f).Size!.Value, 0.01f, "a value sets it");
        Assert.IsNull(sized.With(size: PropertyValue.Inherited).Size, "Inherited clears it");
        Assert.IsNull(sized.With(size: (float?)null).Size, "an explicit null clears it too");
    }

    [TestMethod]
    public void WithWeightHasAllFourStates()
    {
        FontOverride weighted = InheritedFont.WithWeight(500);

        Assert.AreEqual(500, weighted.With(size: 14f).Weight!.Value, "an omitted argument leaves it");
        Assert.AreEqual(700, weighted.With(weight: 700).Weight!.Value, "a value sets it");
        Assert.IsNull(weighted.With(weight: PropertyValue.Inherited).Weight, "Inherited clears it");
        Assert.IsNull(weighted.With(weight: (int?)null).Weight, "an explicit null clears it too");
    }

    [TestMethod]
    public void WithItalicHasAllFourStates()
    {
        FontOverride upright = InheritedFont.WithItalic(false);

        Assert.IsFalse(upright.With(size: 14f).Italic!.Value, "an omitted argument leaves it");
        Assert.IsTrue(upright.With(italic: true).Italic!.Value, "a value sets it");
        Assert.IsNull(upright.With(italic: PropertyValue.Inherited).Italic, "Inherited clears it");
        Assert.IsNull(upright.With(italic: (bool?)null).Italic, "an explicit null clears it too");
    }

    [TestMethod]
    public void InheritedValueOnEveryMemberIsAsIs()
    {
        FontOverride cleared = InheritedFont.With(
            family: PropertyValue.Inherited,
            size: PropertyValue.Inherited,
            weight: PropertyValue.Inherited,
            italic: PropertyValue.Inherited);

        Assert.AreEqual(InheritedFont.AsIs, cleared, "every member is the inherited state, which is AsIs");
        Assert.AreEqual(InheritedFont.AsIs, InheritedFont.With(family: PropertyValue.Inherited));
    }

    [TestMethod]
    public void ClearingAFamilyThroughWithComposesBackToTheInheritedFamily()
    {
        var inherited = new Font(TestFont.Family, 11f, 500);
        FontOverride named = InheritedFont.WithFamily("Segoe UI").WithSize(20f);

        Font composed = Compose(inherited, named.With(family: PropertyValue.Inherited));

        Assert.AreEqual(TestFont.Family, composed.FamilyName, "the inherited family answers again");
        Assert.AreEqual(20f, composed.Size, 0.01f, "and the size the override asked for stays");
        Assert.AreEqual(500, composed.Weight);
    }

    [TestMethod]
    public void AForwardedNullableStringMeansTheDefaultOnFontAndInheritedOnOverride()
    {
        var font = new Font(TestFont.Family, 11f);
        var inherited = new Font(TestFont.Family, 11f);
        string platformDefault = new Font(11f).FamilyName;

        string? forwardedNull = null;

        Assert.AreEqual(
            platformDefault,
            font.With(family: forwardedNull).FamilyName,
            "on a complete font a null family is the platform's default family");

        Font composed = Compose(inherited, InheritedFont.WithFamily("Segoe UI").With(family: forwardedNull));

        Assert.AreEqual(
            TestFont.Family,
            composed.FamilyName,
            "on an override the same null clears the family, so the inherited one answers");

        string? forwardedName = "Segoe UI";

        Assert.AreEqual("Segoe UI", font.With(family: forwardedName).FamilyName);
        Assert.AreEqual("Segoe UI", InheritedFont.With(family: forwardedName).FamilyName);
    }

    [TestMethod]
    public void OverrideWithEveryArgumentOmittedReturnsThisOverride()
    {
        FontOverride sized = InheritedFont.WithSize(14f);

        Assert.AreSame(sized, sized.With(), "a no-op allocates nothing and keeps identity");
    }

    [TestMethod]
    public void InheritedFontWithEveryArgumentOmittedIsAsIs()
    {
        Assert.AreSame(InheritedFont.AsIs, InheritedFont.With(), "the neutral override is the starting point");

        Assert.IsNull(InheritedFont.AsIs.FamilyName);
        Assert.IsNull(InheritedFont.AsIs.Size);
        Assert.IsNull(InheritedFont.AsIs.Weight);
        Assert.IsNull(InheritedFont.AsIs.Italic);

        Assert.AreEqual(
            InheritedFont.AsIs,
            InheritedFont.With(family: null),
            "a supplied null names nothing either, so it is equal to AsIs even though it is a new override");
    }

    [TestMethod]
    public void InheritedFontWithSetsSeveralMembersInOneCall()
    {
        FontOverride oneCall = InheritedFont.With(family: TestFont.Family, size: 14f, weight: 700, italic: true);
        FontOverride chain = InheritedFont
            .WithFamily(TestFont.Family)
            .WithSize(14f)
            .WithWeight(700)
            .WithItalic(true);

        Assert.AreEqual(chain, oneCall, "With names the same members the chain names");
        Assert.AreEqual(InheritedFont.WithSize(14f), InheritedFont.With(size: 14f), "and a single member agrees with its method");
    }

    [TestMethod]
    public void AsIsComposedOntoAFontReturnsThatFont()
    {
        var inherited = new Font(TestFont.Family, 11f);

        Assert.AreSame(inherited, Compose(inherited, InheritedFont.AsIs), "the identity composition returns the font itself");
    }

    [TestMethod]
    public void AsIsWithSizeBehavesLikeInheritedFontWithSize()
    {
        Assert.AreEqual(
            InheritedFont.WithSize(14f),
            InheritedFont.AsIs.WithSize(14f),
            "AsIs is the identity the factory starts from");

        var inherited = new Font(TestFont.Family, 11f);
        Assert.AreEqual(
            Compose(inherited, InheritedFont.WithSize(14f)),
            Compose(inherited, InheritedFont.AsIs.WithSize(14f)),
            "and the composed font is the same description either way");
    }

    [TestMethod]
    public void NullFamilyMeansTheInheritedFamilyOnAnOverride()
    {
        var inherited = new Font(TestFont.Family, 11f);

        Font composed = Compose(inherited, InheritedFont.WithFamily(null));

        Assert.AreEqual(TestFont.Family, composed.FamilyName, "null here keeps the family inherited, unlike Font.WithFamily");
        Assert.AreSame(inherited, composed, "and an override that names nothing composes to the inherited font itself");
    }

    /// <summary>A label that counts how often its effective appearance was invalidated.</summary>
    private sealed class CountingLabel : Label
    {
        public CountingLabel(string text)
            : base(text)
        {
        }

        /// <summary>How many times the appearance was invalidated.</summary>
        public int AppearanceChanges { get; private set; }

        /// <inheritdoc />
        protected override void OnAppearanceChanged() => AppearanceChanges++;
    }

    /// <summary>The font a label resolves to when it inherits <paramref name="inherited"/> and overrides it.</summary>
    private static Font Compose(Font inherited, FontOverride fontOverride)
    {
        var panel = new Panel { Font = inherited };
        var label = new Label("x") { Font = fontOverride };
        panel.Controls.Add(label);

        return label.EffectiveFont;
    }
}

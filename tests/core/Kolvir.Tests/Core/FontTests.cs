using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The fluent modifiers on a complete font description: each one replaces a member and keeps the
/// other three.
/// </summary>
/// <remarks>
/// A <see cref="Font"/> names every member, so there is nothing for a null argument to leave unset:
/// it keeps the value the receiver already has. That is the same rule <see cref="FontOverride.With"/>
/// uses, and it is what makes a modifier a pure description operation rather than a partial one.
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class FontTests
{
    private static readonly Font Original = new(TestFont.Family, 11f, 500, italic: false);

    [TestMethod]
    public void WithReplacesOnlyTheMembersItNames()
    {
        Font derived = Original.With(family: "Courier New", weight: 700);

        Assert.AreEqual("Courier New", derived.FamilyName, "the family is one of the members named");
        Assert.AreEqual(700, derived.Weight, "the weight is the other");
        Assert.AreEqual(Original.Size, derived.Size, 0.01f, "the size keeps the receiver's value");
        Assert.AreEqual(Original.Italic, derived.Italic, "and so does the slant");
    }

    [TestMethod]
    public void WithWithEveryArgumentNullReturnsThisFont()
    {
        Assert.AreSame(Original, Original.With(), "a no-op allocates nothing and keeps identity");
    }

    [TestMethod]
    public void WithSizeChangesOnlyTheSize()
    {
        Font derived = Original.WithSize(19f);

        Assert.AreEqual(19f, derived.Size, 0.01f);
        Assert.AreEqual(Original.FamilyName, derived.FamilyName);
        Assert.AreEqual(Original.Weight, derived.Weight);
        Assert.AreEqual(Original.Italic, derived.Italic);
    }

    [TestMethod]
    public void WithFamilyChangesOnlyTheFamily()
    {
        Font derived = Original.WithFamily("Segoe UI");

        Assert.AreEqual("Segoe UI", derived.FamilyName);
        Assert.AreEqual(Original.Size, derived.Size, 0.01f);
        Assert.AreEqual(Original.Weight, derived.Weight);
        Assert.AreEqual(Original.Italic, derived.Italic);
    }

    [TestMethod]
    public void WithWeightChangesOnlyTheWeight()
    {
        Font derived = Original.WithWeight(300);

        Assert.AreEqual(300, derived.Weight);
        Assert.AreEqual(Original.FamilyName, derived.FamilyName);
        Assert.AreEqual(Original.Size, derived.Size, 0.01f);
        Assert.AreEqual(Original.Italic, derived.Italic);
    }

    [TestMethod]
    public void WithItalicChangesOnlyTheItalic()
    {
        Font derived = Original.WithItalic(true);

        Assert.IsTrue(derived.Italic);
        Assert.AreEqual(Original.FamilyName, derived.FamilyName);
        Assert.AreEqual(Original.Size, derived.Size, 0.01f);
        Assert.AreEqual(Original.Weight, derived.Weight);
    }

    [TestMethod]
    public void NullFamilyMeansTheDefaultOnWithFamilyAndTheReceiversFamilyOnWith()
    {
        var font = new Font(TestFont.Family, 11f);
        string platformDefault = new Font(11f).FamilyName;

        Assert.AreEqual(
            platformDefault,
            font.WithFamily(null).FamilyName,
            "WithFamily(null) is the constructor's null: a complete description names a family, so it names the platform's default one");

        Assert.AreEqual(
            TestFont.Family,
            font.With(family: null).FamilyName,
            "With(family: null) leaves the receiver's family alone, which is the rule every other null argument follows");
    }
}

using System.Globalization;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// A font is a description: what one means, when two of them are the same font, what a shared
/// realization buys, and what the theme does with a family the application registered.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion here is about identity rather than about a particular number: two descriptions of
/// the same face measure the same, an equal description is the same font, the theme adopts a
/// registered family. The one number that would differ between machines - the width of a string - is
/// compared only with itself, which is what makes the suite portable.
/// </para>
/// <para>
/// The registry has tests of its own; this file is about the description and the pool behind it.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class FontLoadingTests : HeadlessTestBase
{
    /// <summary>
    /// The width a font reports for a word is the same however often it is asked, and stays right after
    /// the cache that answers it has started over: the remembering is an optimisation, not a source of
    /// different answers.
    /// </summary>
    [TestMethod]
    public void AMeasuredWidthIsTheSameEveryTimeAndAcrossACacheOverflow()
    {
        var font = new Font("Verdana", 12f);
        string[] words = ["resize", "message", "the", "quick"];

        var first = words.ToDictionary(w => w, w => font.MeasureWidth(w));

        for (int i = 0; i < 200; i++)
        {
            foreach (var word in words)
            {
                Assert.AreEqual(first[word], font.MeasureWidth(word), 0.001f, $"the width of '{word}'");
            }
        }

        // Far more distinct strings than the cache holds, so it starts over while they are being asked.
        for (int i = 0; i < 12000; i++)
        {
            font.MeasureWidth("word" + i.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var word in words)
        {
            Assert.AreEqual(first[word], font.MeasureWidth(word), 0.001f, $"'{word}' after the cache filled");
        }

        // And the answers are still the font's own, not something the cache invented.
        var sameFace = new Font("Verdana", 12f);
        foreach (var word in words)
        {
            Assert.AreEqual(sameFace.MeasureWidth(word), font.MeasureWidth(word), 0.001f, $"'{word}' matches a fresh font");
        }
    }

    private const string SampleText = "The quick brown fox jumps over the lazy dog";
    private const float SampleSize = 13f;

    [TestMethod]
    public void TwoDescriptionsOfOneFaceAreTheSameFont()
    {
        var first = new Font(TestFont.Family, SampleSize);
        var second = new Font(TestFont.Family, SampleSize);

        Assert.AreNotSame(first, second, "they are two objects");
        Assert.IsTrue(first.Equals(second), "and one font");
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode(), "equal fonts hash alike");

        Assert.IsFalse(first.Equals(new Font(TestFont.Family, SampleSize + 1f)), "another size is another font");
        Assert.IsFalse(first.Equals(new Font(TestFont.Family, SampleSize, bold: true)), "another weight is another font");
        Assert.IsFalse(first.Equals(new Font(TestFont.Family, SampleSize, italic: true)), "a slant is another font");
        Assert.IsFalse(first.Equals(new Font("Verdana", SampleSize)), "another family is another font");
        Assert.IsFalse(first.Equals((Font?)null), "and a font is not null");
    }

    [TestMethod]
    public void AFontWithNoFamilyNamesThePlatformsDefaultFamily()
    {
        var fromNull = new Font(null, SampleSize);
        var fromEmpty = new Font(string.Empty, SampleSize);
        var fromSpaces = new Font("   ", SampleSize);

        string defaultFamily = fromNull.FamilyName;

        Assert.IsFalse(
            string.IsNullOrEmpty(defaultFamily),
            "a description is always named, so that it can ask for a weight or a slant");
        Assert.AreEqual(defaultFamily, fromEmpty.FamilyName);
        Assert.AreEqual(defaultFamily, fromSpaces.FamilyName);
        Assert.IsTrue(fromNull.Equals(fromEmpty) && fromEmpty.Equals(fromSpaces), "all three are the one font");
        Assert.IsTrue(
            fromNull.Equals(new Font(defaultFamily, SampleSize)),
            "and naming the default family explicitly is the same description, so the same realization");
        Assert.AreEqual(
            fromNull.MeasureWidth(SampleText),
            fromSpaces.MeasureWidth(SampleText),
            0.001f,
            "and they draw with the same face");
    }

    [TestMethod]
    public void ABoldFontWithNoFamilyIsActuallyBold()
    {
        // The defect behind naming the default family at construction: DirectWrite ignores the
        // requested style when a typeface is asked for by a null family name, so an unnamed
        // description reported weight 700 and drew the regular face.
        var regular = new Font(SampleSize);
        var bold = new Font(SampleSize, bold: true);

        Assert.AreEqual(regular.FamilyName, bold.FamilyName, "the same family, named");
        Assert.AreEqual(
            new Font(regular.FamilyName, SampleSize, 700).MeasureWidth(SampleText),
            bold.MeasureWidth(SampleText),
            0.001f,
            "asking for bold reaches the face now that the family is named");
    }

    [TestMethod]
    public void TheFamilyNameEchoesTheDescriptionRatherThanTheResolvedFace()
    {
        const string missing = "NoSuchFamilyExistsOnAnyDesktop";

        var font = new Font(missing, SampleSize);

        Assert.AreEqual(missing, font.FamilyName, "a family the machine does not have answers the name it was given");
        Assert.AreEqual(
            new Font(SampleSize).MeasureWidth(SampleText),
            font.MeasureWidth(SampleText),
            0.001f,
            "and still draws with the platform's default face");

        var roundTrip = new Font(font.FamilyName, font.Size, font.Weight, font.Italic);
        Assert.IsTrue(font.Equals(roundTrip), "which is what makes the round trip a property");
    }

    [TestMethod]
    public void TheToStringReportsTheDescription()
    {
        string defaultFamily = new Font(13f).FamilyName;

        Assert.AreEqual($"Font({defaultFamily}, 13, w400, upright)", new Font(13f).ToString());
        Assert.AreEqual("Font(Inter, 14, w700, italic)", new Font("Inter", 14f, bold: true, italic: true).ToString());
    }

    [TestMethod]
    public void EqualFontsShareOneRealization()
    {
        var first = new Font(TestFont.Family, SampleSize);
        var second = new Font(TestFont.Family, SampleSize);

        Assert.AreSame(first.Realize(), second.Realize(), "one description, one heavyweight object");

        Assert.AreNotSame(
            first.Realize(),
            new Font(TestFont.Family, SampleSize + 1f).Realize(),
            "and a different size is a different one");
    }

    [TestMethod]
    public void MeasuringEmptyTextRealizesNothing()
    {
        var font = new Font(TestFont.Family, SampleSize);
        int before = FontManager.ApproximateCount;

        Assert.AreEqual(0f, font.MeasureWidth(string.Empty));
        Assert.AreEqual(before, FontManager.ApproximateCount, "an empty string needs no face to measure");
    }

    [TestMethod]
    public void WithSizeKeepsTheFamilyAndOnlyChangesTheSize()
    {
        var loaded = new Font(TestFont.Family, SampleSize);
        var bigger = loaded.WithSize(26f);

        float small = loaded.MeasureWidth(SampleText);
        float large = bigger.MeasureWidth(SampleText);
        float ratio = small == 0f ? 0f : large / small;

        Assert.AreEqual(loaded.FamilyName, bigger.FamilyName, "the derived font keeps the family");
        Assert.AreEqual(26f, bigger.Size);
        Assert.IsTrue(ratio > 1.9f && ratio < 2.1f, $"doubling the size should double the width, ratio {N(ratio)}");
    }

    [TestMethod]
    public void ADerivedFontKeepsTheFamilyItWasGiven()
    {
        // Including the family a description with no family of its own was given when it was made:
        // deriving is arithmetic, and the naming happened once at construction.
        var derived = new Font(SampleSize).WithSize(20f);

        Assert.AreEqual(new Font(SampleSize).FamilyName, derived.FamilyName, "the derived font keeps the family");
        Assert.AreEqual(20f, derived.Size);
    }

    [TestMethod]
    public void ADerivedStyleDoesNotRenameTheFamily()
    {
        // What a rich label's bold span asks for. Naming the default family used to happen here
        // instead, and only when the style changed, because an unnamed description could not express
        // "the default family in bold"; with the name resolved at construction this is arithmetic.
        var plain = new Font(SampleSize);
        var bold = plain.Derive(SampleSize, 700, false);

        Assert.AreEqual(plain.FamilyName, bold.FamilyName, "the family is carried across unchanged");
        Assert.AreEqual(700, bold.Weight);
        Assert.AreEqual(
            new Font(plain.FamilyName, SampleSize, 700).MeasureWidth(SampleText),
            bold.MeasureWidth(SampleText),
            0.001f,
            "and the weight reaches the face");
    }

    [TestMethod]
    public void TheBundledFaceIsNotTheSystemFace()
    {
        var loaded = new Font(TestFont.Family, SampleSize);
        var system = new Font(SampleSize);

        bool differentWidth = loaded.MeasureWidth(SampleText) != system.MeasureWidth(SampleText);

        Assert.IsTrue(
            differentWidth,
            $"the bundled face should measure differently from the machine's: " +
            $"{N(loaded.MeasureWidth(SampleText))} vs {N(system.MeasureWidth(SampleText))}");
    }

    [TestMethod]
    public void AssigningAnEqualFontLaysNothingOutAgain()
    {
        var font = new Font(TestFont.Family, SampleSize);
        var label = new Label("x") { Font = font };

        int epoch = Control.LayoutEpoch;

        label.Font = new Font(TestFont.Family, SampleSize);

        Assert.AreEqual(epoch, Control.LayoutEpoch, "an equal font is the same font, so nothing is laid out again");

        label.Font = new Font(TestFont.Family, SampleSize + 1f);

        Assert.AreNotEqual(epoch, Control.LayoutEpoch, "a font that is not equal is a layout change");
    }

    [TestMethod]
    public void ARegisteredFamilyMakesTheThemeFontsThatFamily()
    {
        string family = TestFont.Register();

        try
        {
            Theme.FontFamily = family;

            Assert.AreEqual(family, Theme.DefaultFont.FamilyName);
            Assert.AreEqual(family, Theme.EmphasizedFont.FamilyName);
            Assert.AreEqual(family, Theme.TitleFont.FamilyName);
            Assert.AreEqual(11.5f, Theme.CaptionFont.Size);
            Assert.AreEqual(24f, Theme.TitleFont.Size);

            // The weight is what the description asked for. One file holds one face, so the tree is
            // drawn in that face and the request is still reported honestly.
            Assert.AreEqual(600, Theme.EmphasizedFont.Weight, "the request is reported, not the file's value");
            Assert.AreEqual(
                new Font(family, 13f).MeasureWidth(SampleText),
                Theme.EmphasizedFont.MeasureWidth(SampleText),
                0.001f,
                "and the face used is the only one the family has");
        }
        finally
        {
            Theme.FontFamily = null;
        }
    }

    [TestMethod]
    public void AssigningAFamilyNameLeavesTheRegisteredFace()
    {
        string family = TestFont.Register();

        try
        {
            Theme.FontFamily = family;
            string registered = Theme.DefaultFont.FamilyName;

            Theme.FontFamily = null;

            Assert.AreNotEqual(registered, Theme.DefaultFont.FamilyName, "the theme should be back on the platform face");
        }
        finally
        {
            Theme.FontFamily = null;
        }
    }

    /// <summary>Formats a metric so a failure message reads the same way on every machine.</summary>
    private static string N(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
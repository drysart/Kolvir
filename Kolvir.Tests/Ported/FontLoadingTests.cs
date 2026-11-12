using System.Globalization;
using System.Text;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The font loaders, ported from the demo application's <c>--font-check</c> harness.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion here is about identity rather than about a particular number: two fonts from the
/// same bytes measure the same, a derived font keeps its face, the theme adopts a loaded face. The
/// one number that would differ between machines - the width of a string - is compared only with
/// itself, which is what makes the suite portable.
/// </para>
/// <para>
/// The harness printed its numbers so a human could diff three platforms' reports. A test cannot
/// print and have anyone read it, so the numbers are folded into the failure messages.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class FontLoadingTests : HeadlessTestBase
{
    private const string SampleText = "The quick brown fox jumps over the lazy dog";
    private const float SampleSize = 13f;

    [TestMethod]
    public void TheBundledFaceIsNotTheSystemFace()
    {
        using var loaded = Font.FromBytes(TestFont.Data);
        using var system = new Font(SampleSize);

        bool differentFace = !string.Equals(loaded.FamilyName, system.FamilyName, StringComparison.Ordinal);
        bool differentWidth = loaded.MeasureWidth(SampleText) != system.MeasureWidth(SampleText);

        Assert.IsTrue(
            differentFace && differentWidth,
            $"the bundled face should not be the machine's: {loaded.FamilyName} vs {system.FamilyName}, " +
            $"{N(loaded.MeasureWidth(SampleText))} vs {N(system.MeasureWidth(SampleText))}");
    }

    [TestMethod]
    public void TwoFontsFromTheSameBytesMeasureIdentically()
    {
        using var loaded = Font.FromBytes(TestFont.Data);
        using var again = Font.FromBytes(TestFont.Data);

        Assert.AreEqual(loaded.MeasureWidth(SampleText), again.MeasureWidth(SampleText), "measure width");
        Assert.AreEqual(loaded.LineHeight, again.LineHeight, "line height");
        Assert.AreEqual(loaded.BaselineOffset, again.BaselineOffset, "baseline offset");
    }

    [TestMethod]
    public void WithSizeKeepsTheLoadedFace()
    {
        using var loaded = Font.FromBytes(TestFont.Data);
        using var bigger = loaded.WithSize(26f);

        float small = loaded.MeasureWidth(SampleText);
        float large = bigger.MeasureWidth(SampleText);
        float ratio = small == 0f ? 0f : large / small;

        Assert.AreEqual(loaded.FamilyName, bigger.FamilyName, "the derived font should keep the face");
        Assert.IsTrue(ratio > 1.9f && ratio < 2.1f, $"doubling the size should double the width, ratio {N(ratio)}");
    }

    [TestMethod]
    public void ADerivedFontOutlivesTheFontItCameFrom()
    {
        using var derived = Build();

        Assert.IsTrue(derived.MeasureWidth(SampleText) > 0f, "a derived font should measure after its original is disposed");

        static Font Build()
        {
            var original = Font.FromBytes(TestFont.Data);
            var child = original.WithSize(20f);
            original.Dispose();
            return child;
        }
    }

    [TestMethod]
    public void DisposingAFontTwiceDoesNothing()
    {
        var twice = Font.FromBytes(TestFont.Data);

        twice.Dispose();
        twice.Dispose();
    }

    [TestMethod]
    public void AFontKeepsNoTieToTheFileItCameFrom()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kolvir-font-{Guid.NewGuid():N}.ttf");
        File.WriteAllBytes(path, TestFont.Data);

        try
        {
            using var fromFile = Font.FromFile(path);
            File.Delete(path);

            Assert.AreEqual(
                fromFile.MeasureWidth(SampleText),
                Font.FromBytes(TestFont.Data).MeasureWidth(SampleText),
                "a font should keep the face's bytes, not the file it read them from");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod]
    public void ANullOrEmptyPathIsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Font.FromFile(string.Empty));
    }

    [TestMethod]
    public void AMissingFileReportsFileNotFoundException()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kolvir-missing-{Guid.NewGuid():N}.ttf");

        Assert.ThrowsExactly<FileNotFoundException>(() => Font.FromFile(path));
    }

    [TestMethod]
    public void AStreamThatIsNotAFontIsRejected()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("This is not a font, it is a sentence."));

        Assert.ThrowsExactly<ArgumentException>(() => Font.FromStream(stream));
    }

    [TestMethod]
    public void AnEmptyStreamIsRejected()
    {
        using var stream = new MemoryStream();

        Assert.ThrowsExactly<ArgumentException>(() => Font.FromStream(stream));
    }

    [TestMethod]
    public void ATruncatedFontIsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Font.FromBytes(TestFont.Data.AsSpan(0, 1024)));
    }

    [TestMethod]
    public void ALoadedFaceMakesTheThemeFontsThatFace()
    {
        // Deliberately not disposed: the theme keeps the face for the rest of the process, which is
        // the documented contract for a font handed to UseFont.
        var loaded = Font.FromBytes(TestFont.Data);

        Theme.UseFont(loaded);

        bool sameFace =
            string.Equals(Theme.DefaultFont.FamilyName, loaded.FamilyName, StringComparison.Ordinal) &&
            string.Equals(Theme.EmphasizedFont.FamilyName, loaded.FamilyName, StringComparison.Ordinal) &&
            string.Equals(Theme.TitleFont.FamilyName, loaded.FamilyName, StringComparison.Ordinal);

        bool sizesKept = Theme.CaptionFont.Size == 11.5f && Theme.TitleFont.Size == 24f;

        // A single file holds a single face, so a semibold theme font must not pretend to be one.
        bool noFakeWeight =
            Theme.EmphasizedFont.Weight == loaded.Weight &&
            Theme.TitleFont.Weight == loaded.Weight;

        Assert.IsTrue(
            sameFace && sizesKept && noFakeWeight,
            $"default {Theme.DefaultFont.FamilyName} {N(Theme.DefaultFont.Size)}, " +
            $"emphasized weight {Theme.EmphasizedFont.Weight}, title {N(Theme.TitleFont.Size)}");
    }

    [TestMethod]
    public void AssigningAFamilyNameDropsTheLoadedFace()
    {
        // Not disposed, for the same reason as above: the theme owns the face it was given.
        Theme.UseFont(Font.FromBytes(TestFont.Data));
        string loadedFace = Theme.DefaultFont.FamilyName;

        Theme.FontFamily = null;

        Assert.AreNotEqual(loadedFace, Theme.DefaultFont.FamilyName, "the theme should be back on the platform face");
    }

    /// <summary>Formats a metric so a failure message reads the same way on every machine.</summary>
    private static string N(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// The registry of application-supplied faces: what a name means, what registration and
/// unregistration invalidate, and what an existing font does when the answer changes under it.
/// </summary>
/// <remarks>
/// Every test registers under a name nothing else uses and unregisters in a <c>finally</c>, because
/// the registry is process-wide: there is one per process by design, since realization happens with no
/// control and therefore no services in sight.
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class FontRegistryTests : HeadlessTestBase
{
    private const string SampleText = "The quick brown fox jumps over the lazy dog";

    /// <summary>
    /// Registers the embedded face before each test, so <see cref="TestFont.Family"/> means the
    /// embedded bytes here rather than a family name the machine does not have.
    /// </summary>
    [TestInitialize]
    public void RegisterTheBundledFace() => TestFont.Register();

    [TestMethod]
    public void AFamilyRegistersUnderTheNameTheCallerChose()
    {
        string family = FontRegistry.Current.Register("Kolvir Test Face", TestFont.Data);

        try
        {
            Assert.AreEqual("Kolvir Test Face", family, "the call reports the name it registered");

            var registered = FontRegistry.Current.Families.Single(f => f.FamilyName == family);
            Assert.AreEqual(1, registered.Faces.Count);
            Assert.AreEqual("IBM Plex Sans", registered.Faces[0].FamilyName, "the file's own name is kept for diagnostics");
            Assert.AreEqual(400, registered.Faces[0].Weight);

            Assert.AreEqual(
                new Font(TestFont.Family, 13f).MeasureWidth(SampleText),
                new Font(family, 13f).MeasureWidth(SampleText),
                0.001f,
                "the registered name draws with the registered bytes");
        }
        finally
        {
            FontRegistry.Current.Unregister(family);
        }
    }

    [TestMethod]
    public void RegisterRefusesANamelessFamily()
    {
        Assert.ThrowsExactly<ArgumentException>(() => FontRegistry.Current.Register(string.Empty, TestFont.Data));
        Assert.ThrowsExactly<ArgumentException>(() => FontRegistry.Current.Register("   ", TestFont.Data));
        Assert.ThrowsExactly<ArgumentNullException>(() => FontRegistry.Current.Register("x", (FontFace)null!));
    }

    [TestMethod]
    public void UnregisterNeverThrowsAndRemovesOnlyOnce()
    {
        Assert.IsFalse(FontRegistry.Current.Unregister(null), "a null name cannot remove anything");
        Assert.IsFalse(FontRegistry.Current.Unregister("  "), "nor can a blank one");
        Assert.IsFalse(FontRegistry.Current.Unregister("Never Registered"), "nor can an unknown one");

        string family = FontRegistry.Current.Register("Kolvir Test Face", TestFont.Data);

        Assert.IsTrue(FontRegistry.Current.Unregister(family), "a registered name removes");
        Assert.IsFalse(FontRegistry.Current.Unregister(family), "and the second call does nothing");
    }

    [TestMethod]
    public void RegisteringTheSameFamilyAgainReplacesTheFace()
    {
        string family = FontRegistry.Current.Register("Kolvir Test Face", TestFont.Data);

        try
        {
            FontRegistry.Current.Register(family, TestFont.Data);

            var registered = FontRegistry.Current.Families.Single(f => f.FamilyName == family);
            Assert.AreEqual(1, registered.Faces.Count, "one name, one face, whatever the call order");
        }
        finally
        {
            FontRegistry.Current.Unregister(family);
        }
    }

    [TestMethod]
    public void AFamilyWithOneRegularFaceServesEveryWeightWithThatFace()
    {
        string family = FontRegistry.Current.Register("Kolvir Test Face", TestFont.Data);

        try
        {
            float regular = new Font(family, 13f).MeasureWidth(SampleText);
            float bold = new Font(family, 13f, bold: true).MeasureWidth(SampleText);

            Assert.AreEqual(regular, bold, 0.001f, "no weight is synthesised and none is invented");
        }
        finally
        {
            FontRegistry.Current.Unregister(family);
        }
    }

    [TestMethod]
    public void ARegisteredNameShadowsTheSystemFamilyOfTheSameName()
    {
        // The platform's own default family, whatever it is called on this machine: the one name every
        // desktop has a face for, so the shadow can be shown without assuming a particular family.
        string systemFamily = SKTypeface.Default.FamilyName!;
        float systemWidth = new Font(systemFamily, 13f).MeasureWidth(SampleText);

        string family = FontRegistry.Current.Register(systemFamily, TestFont.Data);

        try
        {
            Assert.AreEqual(systemFamily, family);
            Assert.AreEqual(
                new Font(TestFont.Family, 13f).MeasureWidth(SampleText),
                new Font(systemFamily, 13f).MeasureWidth(SampleText),
                0.001f,
                "the registered face wins over the platform family of the same name");
        }
        finally
        {
            FontRegistry.Current.Unregister(family);
        }

        Assert.AreEqual(
            systemWidth,
            new Font(systemFamily, 13f).MeasureWidth(SampleText),
            0.001f,
            "and unregistering gives the platform family back");
    }

    [TestMethod]
    public void AnExistingFontResolvesOnceMoreAfterUnregistration()
    {
        string family = FontRegistry.Current.Register("Kolvir Test Face", TestFont.Data);
        var font = new Font(family, 13f);

        try
        {
            float registered = font.MeasureWidth(SampleText);

            FontRegistry.Current.Unregister(family);

            // The description did not change; only what it resolves to did. This is what the epoch
            // comparison in Font.Realize is for.
            Assert.AreNotEqual(
                registered,
                font.MeasureWidth(SampleText),
                "the same font reports the platform face's measurements after the family is gone");
            Assert.AreEqual(
                new Font(13f).MeasureWidth(SampleText),
                font.MeasureWidth(SampleText),
                0.001f,
                "and that is exactly what a name nothing has registered measures");
        }
        finally
        {
            FontRegistry.Current.Unregister(family);
        }
    }

    [TestMethod]
    public void RegisteringAndUnregisteringBumpTheThemeFontVersion()
    {
        int before = Theme.FontVersion;

        string family = FontRegistry.Current.Register("Kolvir Test Face", TestFont.Data);
        int registered = Theme.FontVersion;

        Assert.AreNotEqual(before, registered, "registering changes what a family resolves to, so text is re-measured");

        FontRegistry.Current.Unregister(family);

        Assert.AreNotEqual(registered, Theme.FontVersion, "and so does unregistering");

        int afterUnregister = Theme.FontVersion;
        FontRegistry.Current.Unregister(family);

        Assert.AreEqual(afterUnregister, Theme.FontVersion, "a call that removed nothing changes nothing");
    }

    [TestMethod]
    public void RegisterFromFileReadsAPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kolvir-font-{Guid.NewGuid():N}.ttf");
        File.WriteAllBytes(path, TestFont.Data);

        string family = FontRegistry.Current.RegisterFromFile(path, "Kolvir From File");

        try
        {
            Assert.AreEqual("Kolvir From File", family);
            Assert.AreEqual(
                new Font(TestFont.Family, 13f).MeasureWidth(SampleText),
                new Font(family, 13f).MeasureWidth(SampleText),
                0.001f,
                "the file's face is what the name resolves to");

            File.Delete(path);
            Assert.IsTrue(new Font(family, 13f).MeasureWidth(SampleText) > 0f, "the registry kept the bytes");
        }
        finally
        {
            FontRegistry.Current.Unregister(family);
        }
    }

    [TestMethod]
    public void RegisterFromFileUsesTheFilesOwnFamilyWhenNoneIsGiven()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kolvir-font-{Guid.NewGuid():N}.ttf");
        File.WriteAllBytes(path, TestFont.Data);

        string family = FontRegistry.Current.RegisterFromFile(path);

        try
        {
            Assert.AreEqual("IBM Plex Sans", family, "the family recorded inside the file names it");
        }
        finally
        {
            FontRegistry.Current.Unregister(family);
            File.Delete(path);
        }
    }

    [TestMethod]
    public void RegisterFromFileRefusesAMissingFileAndABadPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kolvir-missing-{Guid.NewGuid():N}.ttf");

        Assert.ThrowsExactly<ArgumentException>(() => FontRegistry.Current.RegisterFromFile(string.Empty));
        Assert.ThrowsExactly<FileNotFoundException>(() => FontRegistry.Current.RegisterFromFile(path));
    }
}
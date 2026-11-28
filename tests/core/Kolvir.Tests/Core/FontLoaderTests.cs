using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Tests;

/// <summary>
/// The font loader's rules: which provider gets asked, what happens when it fails, and what a caller
/// sees when nothing can read the data.
/// </summary>
/// <remarks>
/// The feature packages bring their own decoders and test those; what is pinned here is the seam they
/// plug into - the ordering, the "claiming is a commitment" rule, and the one failure a caller handles.
/// The providers are fakes because the rules are about the loader, not about any format.
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class FontLoaderTests
{
    [TestMethod]
    public void ALoaderWithNoProvidersReadsAPlainFont()
    {
        var face = new FontLoader().LoadFromBytes(TestFont.Data);

        Assert.AreEqual("IBM Plex Sans", face.FamilyName);
        Assert.AreEqual(400, face.Weight);
        Assert.IsFalse(face.Italic);
        CollectionAssert.AreEqual(TestFont.Data, face.Data.ToArray(), "the face keeps the bytes it was read from");
    }

    [TestMethod]
    public void ALoaderWithNoProvidersRefusesAContainerItDoesNotKnow()
    {
        var exception = Assert.ThrowsExactly<FontFormatException>(() => new FontLoader().LoadFromBytes("wOFFnope"u8));

        Assert.AreEqual(string.Empty, exception.ProviderName, "no provider claimed it, so none is named");
        StringAssert.Contains(exception.Message, "not a font this build can read");
    }

    [TestMethod]
    public void AProviderOutranksTheBuiltInLoader()
    {
        var provider = new FakeProvider("fake", priority: 0, canLoad: _ => true, decode: _ => TestFont.Data);

        var face = new FontLoader([provider]).LoadFromBytes(TestFont.Data);

        Assert.AreEqual(1, provider.ClaimCount, "the provider was asked, and claimed what it was given");
        Assert.AreEqual(1, provider.DecodeCount);
        Assert.AreEqual("IBM Plex Sans", face.FamilyName);
    }

    [TestMethod]
    public void TheHigherPriorityProviderWins()
    {
        var low = new FakeProvider("low", priority: 0, canLoad: _ => true, decode: _ => TestFont.Data);
        var high = new FakeProvider("high", priority: 5, canLoad: _ => true, decode: _ => TestFont.Data);

        _ = new FontLoader([low, high]).LoadFromBytes(TestFont.Data);

        Assert.AreEqual(0, low.DecodeCount, "the stronger claim answers");
        Assert.AreEqual(1, high.DecodeCount);
    }

    [TestMethod]
    public void TheLaterRegistrationWinsAtEqualPriority()
    {
        var first = new FakeProvider("first", priority: 0, canLoad: _ => true, decode: _ => TestFont.Data);
        var second = new FakeProvider("second", priority: 0, canLoad: _ => true, decode: _ => TestFont.Data);

        var loader = new FontLoader([first, second]);

        Assert.AreEqual("second", loader.Providers[0].Name, "the list is in the order the loader consults it");

        _ = loader.LoadFromBytes(TestFont.Data);
        Assert.AreEqual(0, first.DecodeCount);
        Assert.AreEqual(1, second.DecodeCount);
    }

    [TestMethod]
    public void AProviderThatThrowsWhileClaimingIsSkipped()
    {
        var broken = new FakeProvider(
            "broken", priority: 10, canLoad: _ => throw new InvalidOperationException("boom"), decode: _ => TestFont.Data);
        var working = new FakeProvider("working", priority: 0, canLoad: _ => true, decode: _ => TestFont.Data);

        var face = new FontLoader([broken, working]).LoadFromBytes(TestFont.Data);

        Assert.AreEqual("IBM Plex Sans", face.FamilyName, "identifying a stream must not be able to fail a load");
        Assert.AreEqual(1, working.DecodeCount);
    }

    [TestMethod]
    public void AClaimingProviderThatFailsIsNamedAndDoesNotFallThrough()
    {
        var broken = new FakeProvider(
            "broken", priority: 10, canLoad: _ => true, decode: _ => throw new InvalidDataException("the table is truncated"));
        var next = new FakeProvider("next", priority: 0, canLoad: _ => true, decode: _ => TestFont.Data);

        var exception = Assert.ThrowsExactly<FontFormatException>(
            () => new FontLoader([broken, next]).LoadFromBytes(TestFont.Data));

        Assert.AreEqual("broken", exception.ProviderName);
        Assert.IsInstanceOfType<InvalidDataException>(exception.InnerException);
        Assert.AreEqual(0, next.DecodeCount, "a claimed stream is not handed to another parser");
    }

    [TestMethod]
    public void AProviderThatReturnsSomethingSkiaCannotReadIsItsOwnFailure()
    {
        var provider = new FakeProvider("fake", priority: 0, canLoad: _ => true, decode: _ => "not a font"u8.ToArray());

        var exception = Assert.ThrowsExactly<FontFormatException>(
            () => new FontLoader([provider]).LoadFromBytes(TestFont.Data));

        Assert.AreEqual("fake", exception.ProviderName, "it said the data was its format");
        Assert.AreEqual(1, provider.DecodeCount);
    }

    [TestMethod]
    public void AProviderThatReturnsNothingIsAFailure()
    {
        var provider = new FakeProvider("fake", priority: 0, canLoad: _ => true, decode: _ => []);

        var exception = Assert.ThrowsExactly<FontFormatException>(
            () => new FontLoader([provider]).LoadFromBytes(TestFont.Data));

        Assert.AreEqual("fake", exception.ProviderName);
    }

    [TestMethod]
    public void AProviderWithoutANameIsRefused()
    {
        var nameless = new FakeProvider(" ", priority: 0, canLoad: _ => true, decode: _ => TestFont.Data);

        Assert.ThrowsExactly<ArgumentException>(() => new FontLoader([nameless]));
    }

    [TestMethod]
    public void APathAndAStreamReadTheSameFace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kolvir-font-{Guid.NewGuid():N}.ttf");
        File.WriteAllBytes(path, TestFont.Data);

        try
        {
            var loader = new FontLoader();

            var fromPath = loader.LoadFromPath(path);
            var fromStream = loader.LoadFromStream(new MemoryStream(TestFont.Data));

            Assert.AreEqual(fromPath.FamilyName, fromStream.FamilyName);
            CollectionAssert.AreEqual(fromPath.Data.ToArray(), fromStream.Data.ToArray(), "one face, two ways in");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AnEmptyBufferIsAFailure()
    {
        Assert.ThrowsExactly<FontFormatException>(() => new FontLoader().LoadFromBytes([]));
    }

    [TestMethod]
    public void AFaceWithNoSizeIsTheSameFaceAtEverySize()
    {
        // The reason a load produces a face rather than a font: the file has no size, so the caller
        // picks one, and two sizes of the same family measure in proportion.
        string family = FontRegistry.Current.Register("Loader face", new FontLoader().LoadFromBytes(TestFont.Data));

        try
        {
            var small = new Font(family, 10f).MeasureWidth("Handgloves");
            var large = new Font(family, 20f).MeasureWidth("Handgloves");

            Assert.AreEqual(2f, large / small, 0.01f, "doubling the size doubles the width");
        }
        finally
        {
            FontRegistry.Current.Unregister(family);
        }
    }

    /// <summary>A provider that answers from the test's own bytes and counts what it was asked.</summary>
    private sealed class FakeProvider(
        string name,
        int priority,
        Func<ReadOnlySpan<byte>, bool> canLoad,
        Func<byte[], byte[]> decode) : IFontFormatProvider
    {
        public string Name => name;

        public int Priority => priority;

        public int ClaimCount { get; private set; }

        public int DecodeCount { get; private set; }

        public bool CanLoad(ReadOnlySpan<byte> header)
        {
            ClaimCount++;
            return canLoad(header);
        }

        public ReadOnlyMemory<byte> Decode(ReadOnlyMemory<byte> data)
        {
            DecodeCount++;
            return decode(data.ToArray());
        }
    }
}

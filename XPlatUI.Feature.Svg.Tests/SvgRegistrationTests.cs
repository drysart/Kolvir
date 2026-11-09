using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace XPlatUI.Svg.Tests;

/// <summary>
/// The opt-in: what registering the provider does to one factory, what a container does for an
/// application, and what the factory does with providers that fail or decline.
/// </summary>
/// <remarks>
/// Registration is a startup concern here, so these tests compose a factory rather than mutate a
/// process-wide list: <c>services.AddSvgImages()</c> adds a provider to a collection, and the factory
/// the application resolves is built from what that collection holds.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Svg")]
public sealed class SvgRegistrationTests : SvgTestBase
{
    [TestMethod]
    public void WithoutTheProviderAnSvgIsNotAnImage()
    {
        // A factory with no SVG provider: the feature is referenced and in the output, and the bytes
        // are simply not an image, exactly as an unknown raster is not.
        var factory = new XPlatImageDataFactory();

        var failure = Assert.ThrowsExactly<XPlatImageFormatException>(
            () => factory.LoadFromStream(SvgFixtures.Stream(SvgFixtures.Square)));

        Assert.AreEqual(string.Empty, failure.ProviderName, "nothing claimed the bytes");
    }

    [TestMethod]
    public void OneFactoryLoadsSvgAndPngWithTheSameCall()
    {
        // The one-stop-shop claim, tested as itself: no branching at the call site, and one factory
        // produces both.
        using var svg = Factory.LoadFromStream(SvgFixtures.Stream(SvgFixtures.Square));
        using var png = Factory.LoadFromStream(new MemoryStream(SvgFixtures.Png(SvgFixtures.Blue)));

        Assert.AreEqual(10f, svg.NaturalSize.Width, 0.01f, "the SVG came back at the document's size");
        Assert.AreEqual(4f, png.NaturalSize.Width, 0.5f, "and the PNG at the file's");
    }

    [TestMethod]
    public void TheServiceCollectionOptInPutsTheProviderInTheContainer()
    {
        var services = new ServiceCollection();
        var returned = services.AddSvgImages();

        Assert.AreSame(services, returned, "the call chains");

        using var provider = services.BuildServiceProvider();
        var formats = provider.GetServices<IXPlatImageFormatProvider>().ToArray();

        Assert.AreEqual(1, formats.Length, "the container holds the SVG provider");
        Assert.AreSame(XPlatSvgImageFormatProvider.Shared, formats[0]);

        using var data = new XPlatImageDataFactory(formats).LoadFromStream(SvgFixtures.Stream(SvgFixtures.Square));
        Assert.AreEqual(10f, data.NaturalSize.Width, 0.01f, "and a factory built from it reads SVG");
    }

    [TestMethod]
    public void AnApplicationResolvesAFactoryBuiltFromItsDeclaredFormats()
    {
        // What an application actually does: declare the feature, resolve the factory, assign the
        // result. This is the path the demo takes, minus the window.
        using var scene = SvgScene.Create(
            new XPlatPanel(),
            10f,
            10f,
            services => services.AddSvgImages());

        var factory = XPlatApplication.Current!.Services.GetRequiredService<IXPlatImageDataFactory>();
        using var data = factory.LoadFromStream(SvgFixtures.Stream(SvgFixtures.Square));

        Assert.AreEqual(10f, data.NaturalSize.Width, 0.01f, "the application's factory knows about SVG");
    }

    [TestMethod]
    public void AProviderThatThrowsFailsTheLoadAndNamesItself()
    {
        var factory = FactoryWith(XPlatSvgImageFormatProvider.Shared, new ThrowingProvider());

        var failure = Assert.ThrowsExactly<XPlatImageFormatException>(
            () => factory.LoadFromStream(new MemoryStream(ThrowingProvider.Bytes())));

        Assert.AreEqual("throwing", failure.ProviderName, "the failure names the provider that claimed the bytes");
        Assert.IsInstanceOfType<InvalidOperationException>(failure.InnerException, "and keeps what it threw");
    }

    [TestMethod]
    public void AProviderThatClaimsNothingIsNeverAskedToDecode()
    {
        var fussy = new FussyProvider();
        var factory = FactoryWith(XPlatSvgImageFormatProvider.Shared, fussy);

        using var data = factory.LoadFromStream(SvgFixtures.Stream(SvgFixtures.Square));

        Assert.AreEqual(10f, data.NaturalSize.Width, 0.01f, "the provider that claimed the bytes produced the data");
        Assert.AreEqual(0, fussy.Decodes, "and the one that declined was never asked to decode");
    }

    [TestMethod]
    public void ARegisteredProviderOutranksTheSvgProviderForTheBytesItClaims()
    {
        // Priority, not order: the replacing provider claims SVG bytes above the SVG provider, so it
        // is the one that decodes them.
        var replacing = new ReplacingProvider();
        var factory = FactoryWith(XPlatSvgImageFormatProvider.Shared, replacing);

        using var data = factory.LoadFromStream(SvgFixtures.Stream(SvgFixtures.Square));

        Assert.AreEqual(ReplacingProvider.Size, data.NaturalSize.Width, 0.01f, "the higher priority provider produced it");
    }

    /// <summary>Claims a private magic number and throws when asked to decode it.</summary>
    private sealed class ThrowingProvider : IXPlatImageFormatProvider
    {
        internal static byte[] Bytes() => [.. "THROW"u8, .. new byte[16]];

        public string Name => "throwing";

        public int Priority => 10;

        public bool CanDecode(ReadOnlySpan<byte> header) => header.StartsWith("THROW"u8);

        public XPlatImageData Create(Stream stream, string? extensionHint, IXPlatImageFormatOptions? options) =>
            throw new InvalidOperationException("this provider is broken on purpose");
    }

    /// <summary>Claims nothing at all, and counts being asked.</summary>
    private sealed class FussyProvider : IXPlatImageFormatProvider
    {
        internal int Decodes { get; private set; }

        public string Name => "fussy";

        public int Priority => 50;

        public bool CanDecode(ReadOnlySpan<byte> header) => false;

        public XPlatImageData Create(Stream stream, string? extensionHint, IXPlatImageFormatOptions? options)
        {
            Decodes++;
            return null!;
        }
    }

    /// <summary>Takes over SVG and draws a fixed square, so the difference is visible in the bounds.</summary>
    private sealed class ReplacingProvider : IXPlatImageFormatProvider
    {
        internal const float Size = 33f;

        public string Name => "replacing";

        public int Priority => 500;

        public bool CanDecode(ReadOnlySpan<byte> header) => XPlatSvgDocument.LooksLikeSvg(header);

        public XPlatImageData Create(Stream stream, string? extensionHint, IXPlatImageFormatOptions? options)
        {
            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new SKRect(0f, 0f, Size, Size));
            using var paint = new SKPaint { Color = SKColors.Magenta };
            canvas.DrawRect(0f, 0f, Size, Size, paint);
            return XPlatImageData.FromPicture(recorder.EndRecording(), disposePictureWithData: true);
        }
    }
}

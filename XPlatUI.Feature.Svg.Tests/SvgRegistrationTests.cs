using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace XPlatUI.Svg.Tests;

/// <summary>
/// The opt-in: the direct call, the service-collection call, and what unregistering does to work
/// that is already on screen.
/// </summary>
/// <remarks>
/// An image format is process state, so these tests are about a list rather than a container: the
/// call adds a provider, the handle takes it away, and a picture already handed to a control is not
/// affected either way.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Svg")]
public sealed class SvgRegistrationTests : SvgTestBase
{
    [TestMethod]
    public void WithoutTheOptInAnSvgStreamIsNotClaimed()
    {
        // Nothing registered: the feature is referenced and in the process, and a load of an SVG is
        // simply an unknown format - false, no failure reported, exactly as an unknown raster is.
        using var image = new XPlatImage();
        int failures = 0;
        image.AddLoadFailedHandler((_, _) => failures++);

        Assert.IsFalse(image.Load(SvgFixtures.Stream(SvgFixtures.Square)), "SVG is not read until it is turned on");
        Assert.AreEqual(0, failures, "and an unclaimed stream reports nothing");
        Assert.IsNull(image.Picture, "nothing was drawn");
    }

    [TestMethod]
    public void AddSvgImagesTurnsItOnAndTheHandleTurnsItOffAgain()
    {
        using (XPlatSvg.AddSvgImages())
        {
            using var image = new XPlatImage();
            Assert.IsTrue(image.Load(SvgFixtures.Stream(SvgFixtures.Square)), "a registered provider claims the bytes");
        }

        using var after = new XPlatImage();
        Assert.IsFalse(after.Load(SvgFixtures.Stream(SvgFixtures.Square)), "and disposing the handle stops future loads");
    }

    [TestMethod]
    public void RegisteringTwiceRegistersOnce()
    {
        var first = XPlatSvg.AddSvgImages();
        var second = XPlatSvg.AddSvgImages();

        // Disposing one handle must not unregister a provider the other still claims.
        first.Dispose();
        using var image = new XPlatImage();
        Assert.IsTrue(image.Load(SvgFixtures.Stream(SvgFixtures.Square)), "the second registration still stands");

        second.Dispose();
        using var after = new XPlatImage();
        Assert.IsFalse(after.Load(SvgFixtures.Stream(SvgFixtures.Square)), "and the last one to go turns it off");
    }

    [TestMethod]
    public void TheServiceCollectionOptInRegistersIt()
    {
        // The DI form exists so the opt-in sits where every other feature's does. It is the same
        // registration, so it outlives the collection that carried it - which is the behaviour the
        // class documents and this test pins.
        var services = new ServiceCollection();
        var returned = services.AddSvgImages();

        Assert.AreSame(services, returned, "the call chains");

        using var image = new XPlatImage();
        Assert.IsTrue(image.Load(SvgFixtures.Stream(SvgFixtures.Square)), "SVG is on");

        // Put the process back: the registration is not owned by a container.
        var provider = new XPlatSvgImageFormatProvider();
        Assert.IsTrue(XPlatImageFormats.Remove(XPlatSvgImageFormatProvider.Shared), "the provider was on the list");
    }

    [TestMethod]
    public void UnregisteringDoesNotDisturbAPictureThatIsAlreadyLoaded()
    {
        var handle = RegisterSvg();
        using var image = new XPlatImage { Dock = DockStyle.Fill, SizeMode = XPlatSizeMode.StretchImage };
        Assert.IsTrue(image.Load(SvgFixtures.Stream(SvgFixtures.Square)), "loads while registered");

        handle.Dispose();

        using var scene = SvgScene.Create(image, 40f, 40f);
        Assert.AreEqual(SvgFixtures.Blue, scene.PixelAt(20, 20), "and still draws after the provider is gone");
    }

    [TestMethod]
    public void AProviderThatThrowsFailsTheLoadAndNamesItself()
    {
        Register(new ThrowingProvider());
        using var image = new XPlatImage();
        Exception? reported = null;
        using var subscription = image.AddLoadFailedHandler((_, e) => reported = e);

        Assert.IsFalse(image.Load(new MemoryStream(ThrowingProvider.Bytes())), "a throwing provider fails the load");

        var failure = (XPlatImageFormatException)reported!;
        Assert.AreEqual("throwing", failure.ProviderName, "the failure names the provider that claimed the bytes");
        Assert.IsInstanceOfType<InvalidOperationException>(failure.InnerException, "and keeps what it threw");
    }

    [TestMethod]
    public void AProviderThatClaimsNothingIsNeverAskedToDecode()
    {
        var provider = new FussyProvider();
        Register(provider);
        using var image = new XPlatImage();
        int failures = 0;
        image.AddLoadFailedHandler((_, _) => failures++);

        Assert.IsFalse(image.Load(SvgFixtures.Stream(SvgFixtures.Square)), "nothing claimed what an SVG is not");
        Assert.AreEqual(0, provider.Decodes, "and the provider was never asked to decode");
        Assert.AreEqual(0, failures, "so nothing was reported");
    }

    [TestMethod]
    public void ARegisteredProviderOutranksTheBuiltInRasterDecoder()
    {
        // The built-in decoder is priority 0, so a provider above it wins even for bytes both could
        // read. This is how an application replaces a decoder it does not like.
        var provider = new ReplacingProvider();
        Register(provider);

        using var image = new XPlatImage();
        Assert.IsTrue(image.Load(new MemoryStream(SvgFixtures.Png(SvgFixtures.Blue))), "the PNG loads");
        Assert.AreEqual(ReplacingProvider.Size, image.Picture!.CullRect.Width, 0.01f, "through the registered provider");
    }

    /// <summary>Claims a private magic number and throws when asked to decode it.</summary>
    private sealed class ThrowingProvider : IXPlatImageFormatProvider
    {
        internal static byte[] Bytes() => [.. "THROW"u8, .. new byte[16]];

        public string Name => "throwing";

        public int Priority => 10;

        public bool CanDecode(ReadOnlySpan<byte> header) => header.StartsWith("THROW"u8);

        public SKPicture? Decode(Stream stream, string? extensionHint) =>
            throw new InvalidOperationException("this provider is broken on purpose");
    }

    /// <summary>Claims nothing at all, and counts being asked.</summary>
    private sealed class FussyProvider : IXPlatImageFormatProvider
    {
        internal int Decodes { get; private set; }

        public string Name => "fussy";

        public int Priority => 50;

        public bool CanDecode(ReadOnlySpan<byte> header) => false;

        public SKPicture? Decode(Stream stream, string? extensionHint)
        {
            Decodes++;
            return null;
        }
    }

    /// <summary>Takes over PNG and draws a fixed square, so the difference is visible in the bounds.</summary>
    private sealed class ReplacingProvider : IXPlatImageFormatProvider
    {
        internal const float Size = 33f;

        public string Name => "replacing";

        public int Priority => 5;

        public bool CanDecode(ReadOnlySpan<byte> header) => header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47]);

        public SKPicture? Decode(Stream stream, string? extensionHint)
        {
            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new SKRect(0f, 0f, Size, Size));
            using var paint = new SKPaint { Color = SKColors.Magenta };
            canvas.DrawRect(0f, 0f, Size, Size, paint);
            return recorder.EndRecording();
        }
    }
}

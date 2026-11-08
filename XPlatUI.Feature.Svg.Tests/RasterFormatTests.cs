using SkiaSharp;

namespace XPlatUI.Svg.Tests;

/// <summary>
/// The raster formats the control has always read, checked with the SVG feature present in the
/// process but nothing registered.
/// </summary>
/// <remarks>
/// This is the "core stays able to decode with nothing installed" claim, and the only way to test it
/// honestly from this assembly is to register nothing: the feature is referenced, the code is in the
/// output, and the provider is not on the list, so every one of these loads goes through the same
/// built-in raster decoder an application without the feature uses.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Svg")]
public sealed class RasterFormatTests : SvgTestBase
{
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public void EveryRasterFormatTheControlAlwaysReadStillReads()
    {
        var fixtures = new (string Name, byte[] Bytes, int Width, int Height)[]
        {
            ("PNG", SvgFixtures.Png(SvgFixtures.Blue), 4, 2),
            ("JPEG", SvgFixtures.Jpeg(SvgFixtures.Blue), 4, 2),
            ("WebP", SvgFixtures.Webp(SvgFixtures.Blue), 4, 2),
            ("GIF", SvgFixtures.Gif(), 1, 1),
            ("BMP", SvgFixtures.Bmp(), 2, 2),
        };

        foreach (var (name, bytes, width, height) in fixtures)
        {
            using var image = new XPlatImage();
            int failures = 0;
            image.AddLoadFailedHandler((_, _) => failures++);

            Assert.IsTrue(image.Load(new MemoryStream(bytes)), $"{name} should decode with no provider registered");
            Assert.IsNotNull(image.Picture, $"{name} should produce a picture");
            Assert.AreEqual(width, image.Picture!.CullRect.Width, 0.5f, $"{name}'s width is the file's");
            Assert.AreEqual(height, image.Picture.CullRect.Height, 0.5f, $"{name}'s height is the file's");
            Assert.AreEqual(0, failures, $"{name} should not report a failure");
        }
    }

    [TestMethod]
    public void ABitmapIsNotClaimedByTheSvgProvider()
    {
        // The two never contend in practice, and this is the assertion behind that sentence: given
        // PNG bytes, the SVG provider declines.
        var provider = XPlatSvgImageFormatProvider.Shared;

        Assert.IsFalse(provider.CanDecode(SvgFixtures.Png(SvgFixtures.Blue)), "a PNG is not an SVG");
        Assert.IsFalse(provider.CanDecode(SvgFixtures.Bmp()), "nor is a BMP");
        Assert.IsTrue(provider.CanDecode(SvgFixtures.Bytes(SvgFixtures.Square)), "and an SVG is");
    }

    [TestMethod]
    public void AnUnknownStreamIsAFalseReturnWithNoFailureReported()
    {
        // Unchanged from before the seam existed: a stream that is not a recognised image is not an
        // error to report, it is simply not an image.
        using var image = new XPlatImage();
        int failures = 0;
        image.AddLoadFailedHandler((_, _) => failures++);

        Assert.IsFalse(image.Load(new MemoryStream([1, 2, 3, 4])), "four bytes of nothing are not an image");
        Assert.AreEqual(0, failures, "and nothing is raised for them");
    }

    [TestMethod]
    public void ANonSeekableStreamIsBufferedAndStillDecodes()
    {
        using var image = new XPlatImage();

        Assert.IsTrue(image.Load(new ForwardOnlyStream(SvgFixtures.Png(SvgFixtures.Blue))), "a pipe-like stream should decode");
        Assert.AreEqual(4f, image.Picture!.CullRect.Width, "by the same bounds as a seekable one");
    }

    /// <summary>A stream that can only be read forwards, like a socket or a pipe.</summary>
    private sealed class ForwardOnlyStream(byte[] bytes) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int available = Math.Min(count, bytes.Length - _position);
            Array.Copy(bytes, _position, buffer, offset, available);
            _position += available;
            return available;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

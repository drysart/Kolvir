namespace Kolvir.Svg.Tests;

/// <summary>
/// The raster formats the built-in decoder has always read, checked from the assembly that also
/// carries the SVG feature, with no provider in the factory.
/// </summary>
/// <remarks>
/// This is the "core stays able to decode with nothing installed" claim, and the only way to test it
/// honestly from this assembly is to pass no providers: the feature is referenced, the code is in the
/// output, and the factory still reads every raster format - which is the built-in decoder doing
/// fallback work rather than a provider.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Svg")]
public sealed class RasterFormatTests : SvgTestBase
{
    [TestMethod]
    public void EveryRasterFormatTheControlAlwaysReadStillReads()
    {
        var factory = new ImageDataFactory();

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
            using var data = factory.LoadFromBytes(bytes);

            Assert.AreEqual(width, data.NaturalSize.Width, 0.5f, $"{name}'s width is the file's");
            Assert.AreEqual(height, data.NaturalSize.Height, 0.5f, $"{name}'s height is the file's");
            Assert.AreEqual(TimeSpan.Zero, data.Duration, $"{name} is a still image");
        }
    }

    [TestMethod]
    public void ABitmapIsNotClaimedByTheSvgProvider()
    {
        // The two never contend in practice, and this is the assertion behind that sentence: given
        // PNG bytes, the SVG provider declines.
        var provider = SvgImageFormatProvider.Shared;

        Assert.IsFalse(provider.CanDecode(SvgFixtures.Png(SvgFixtures.Blue)), "a PNG is not an SVG");
        Assert.IsFalse(provider.CanDecode(SvgFixtures.Bmp()), "nor is a BMP");
        Assert.IsTrue(provider.CanDecode(SvgFixtures.Bytes(SvgFixtures.Square)), "and an SVG is");
    }

    [TestMethod]
    public void AnUnknownStreamIsAFailureWithNoProviderNamed()
    {
        var failure = Assert.ThrowsExactly<ImageFormatException>(
            () => new ImageDataFactory().LoadFromBytes([1, 2, 3, 4]));

        Assert.AreEqual(string.Empty, failure.ProviderName, "nothing claimed the bytes");
    }

    [TestMethod]
    public void ANonSeekableStreamIsBufferedAndStillDecodes()
    {
        using var data = new ImageDataFactory().LoadFromStream(
            new ForwardOnlyStream(SvgFixtures.Png(SvgFixtures.Blue)));

        Assert.AreEqual(4f, data.NaturalSize.Width, "by the same bounds as a seekable one");
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

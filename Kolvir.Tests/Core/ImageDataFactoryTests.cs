using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// The factory: what a provider may be, which one wins when several claim the same bytes, what a
/// failing one does, and how bytes nobody claims are handled.
/// </summary>
/// <remarks>
/// These are core behaviours, so they live in the core suite rather than in a feature's. An SVG
/// provider is one caller of the seam; the seam's own rules have to hold for a provider the library
/// has never heard of, which is what the stand-ins here are.
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class ImageDataFactoryTests
{
    [TestMethod]
    public void TheBuiltInDecoderNeedsNoProvider()
    {
        // The control has to read a PNG with nothing installed. The built-in decoder is fallback
        // behaviour, not an entry in the list, so an empty factory still decodes raster bytes.
        using var data = new ImageDataFactory().LoadFromBytes(Png(4, 2));

        Assert.AreEqual(TimeSpan.Zero, data.Duration, "a raster image is a still picture");
        Assert.AreEqual(4f, data.NaturalSize.Width, "at the file's size");
        Assert.AreEqual(2f, data.NaturalSize.Height);
        Assert.AreEqual(0, new ImageDataFactory().Providers.Count, "and no provider was involved");
    }

    [TestMethod]
    public void BytesNobodyClaimsAreAFailureThatNamesNoProvider()
    {
        var failure = Assert.ThrowsExactly<ImageFormatException>(
            () => new ImageDataFactory().LoadFromBytes([1, 2, 3, 4]));

        Assert.AreEqual(string.Empty, failure.ProviderName, "no provider claimed the bytes, so none is blamed");
        Assert.IsNull(failure.InnerException);
    }

    [TestMethod]
    public void ARegisteredProviderIsConsultedBeforeTheBuiltInDecoder()
    {
        // The rule the whole seam exists for: registering a provider takes its format away from the
        // built-in decoder rather than competing with it.
        var provider = new StandIn("magic", priority: 1, magic: string.Empty, width: 11f);
        var factory = new ImageDataFactory([provider]);

        using var data = factory.LoadFromBytes(Png(4, 2));

        Assert.AreEqual(11f, data.NaturalSize.Width, "the provider's data was used");
        Assert.AreEqual(1, provider.Creates, "and it was asked exactly once");
    }

    [TestMethod]
    public void AHigherPriorityProviderWins()
    {
        // Priority outranks recency, whichever order the two were registered in.
        foreach (var (first, second) in new[]
        {
            (new StandIn("lower", priority: 1, magic: "MAGIC", width: 11f), new StandIn("higher", priority: 2, magic: "MAGIC", width: 22f)),
            (new StandIn("higher", priority: 2, magic: "MAGIC", width: 22f), new StandIn("lower", priority: 1, magic: "MAGIC", width: 11f)),
        })
        {
            using var data = new ImageDataFactory([first, second]).LoadFromStream(Stream("MAGIC"));

            Assert.AreEqual(22f, data.NaturalSize.Width, "the higher priority provider produced the data");
            Assert.AreEqual(0, first.Creates + second.Creates - 1, "and the other one was never asked to decode");
        }
    }

    [TestMethod]
    public void AtEqualPriorityTheLaterRegistrationWins()
    {
        // The rule the registry this replaced had, unchanged: an application replaces a provider by
        // registering another at the same priority, and the last one declared is the one that
        // answers. Registration order is the order the list arrives in.
        var first = new StandIn("aaa", priority: 5, magic: "MAGIC", width: 11f);
        var second = new StandIn("bbb", priority: 5, magic: "MAGIC", width: 22f);

        using var data = new ImageDataFactory([first, second]).LoadFromStream(Stream("MAGIC"));

        Assert.AreEqual(22f, data.NaturalSize.Width, "the later registration wins at equal priority");
        Assert.AreEqual(0, first.Creates, "the shadowed provider was never asked to decode");
    }

    [TestMethod]
    public void AProviderThatDeclinesIsSkippedAndNeverAskedToDecode()
    {
        var shy = new StandIn("shy", priority: 100, magic: "OTHER", width: 11f);
        var factory = new ImageDataFactory([shy]);

        // A PNG is still readable by the built-in decoder, and the provider that said no was not
        // consulted for it at all.
        using var data = factory.LoadFromBytes(Png(4, 2));

        Assert.AreEqual(4f, data.NaturalSize.Width);
        Assert.AreEqual(0, shy.Creates, "a provider that declined was never asked to decode");
    }

    [TestMethod]
    public void AClaimingProviderOwnsItsFailureAndIsNotRetried()
    {
        var thrower = new StandIn("thrower", priority: 3, magic: "MAGIC", width: 11f) { Throws = true };
        var fallback = new StandIn("fallback", priority: 2, magic: "MAGIC", width: 22f);
        var factory = new ImageDataFactory([thrower, fallback]);

        var failure = Assert.ThrowsExactly<ImageFormatException>(() => factory.LoadFromStream(Stream("MAGIC")));

        Assert.AreEqual("thrower", failure.ProviderName, "the provider that claimed the bytes is named");
        Assert.IsInstanceOfType<InvalidOperationException>(failure.InnerException);
        Assert.AreEqual(0, fallback.Creates, "and the next claim was not tried");
    }

    [TestMethod]
    public void AProviderThatReturnsNothingFailsTheLoad()
    {
        var empty = new StandIn("empty", priority: 3, magic: "MAGIC", width: 11f) { ReturnsNull = true };

        var failure = Assert.ThrowsExactly<ImageFormatException>(
            () => new ImageDataFactory([empty]).LoadFromStream(Stream("MAGIC")));

        Assert.AreEqual("empty", failure.ProviderName);
        Assert.IsNull(failure.InnerException, "nothing was thrown, so there is nothing to wrap");
    }

    [TestMethod]
    public void AProviderThatThrowsWhileSniffingHasNotClaimedTheBytes()
    {
        // Identifying a stream runs for every provider, so a bug there must not be able to fail a
        // load it never claimed.
        var broken = new StandIn("broken-sniff", priority: 9, magic: "MAGIC", width: 11f) { ThrowsFromCanDecode = true };
        var factory = new ImageDataFactory([broken]);

        using var png = factory.LoadFromBytes(Png(4, 2));
        Assert.AreEqual(4f, png.NaturalSize.Width, "a PNG still loads past a provider that threw");

        Assert.ThrowsExactly<ImageFormatException>(
            () => factory.LoadFromStream(Stream("MAGIC")),
            "and bytes nobody else claims are still a failure");

        Assert.AreEqual(0, broken.Creates, "the broken provider was never asked to decode");
    }

    [TestMethod]
    public void TheOptionsReachTheProviderThatClaimsTheBytes()
    {
        var provider = new StandIn("options", priority: 3, magic: "MAGIC", width: 11f);
        var options = new TestOptions();

        using var data = new ImageDataFactory([provider]).LoadFromStream(Stream("MAGIC"), options);

        Assert.AreSame(options, provider.LastOptions, "the claiming provider saw exactly what was passed");
    }

    [TestMethod]
    public void AClaimingProviderRefusesOptionsItDoesNotUnderstand()
    {
        // Silently ignoring a setting the caller took the trouble to pass is how "my option does
        // nothing" gets reported, so the built-in fallback refuses anything at all.
        var options = new TestOptions();

        var failure = Assert.ThrowsExactly<ImageFormatException>(
            () => new ImageDataFactory().LoadFromBytes(Png(4, 2), options));

        Assert.AreEqual(string.Empty, failure.ProviderName, "the built-in decoder is not a provider");
        Assert.IsInstanceOfType<ArgumentException>(failure.InnerException);
    }

    [TestMethod]
    public void AnUnseekableStreamIsBufferedRatherThanRefused()
    {
        // A network stream or a pipe is the case a real application hits, and refusing it would be a
        // surprise. The bytes are copied once and decoded from the copy.
        using var data = new ImageDataFactory().LoadFromStream(new UnseekableStream(Png(6, 3)));

        Assert.AreEqual(6f, data.NaturalSize.Width);
        Assert.AreEqual(3f, data.NaturalSize.Height);
    }

    [TestMethod]
    public void AnUnseekableStreamOverTheBufferLimitIsRefused()
    {
        var factory = new ImageDataFactory { BufferLimit = 16 };

        var failure = Assert.ThrowsExactly<ImageFormatException>(
            () => factory.LoadFromStream(new UnseekableStream(Png(40, 40))));

        Assert.AreEqual(string.Empty, failure.ProviderName, "the limit is the factory's, not a format's");
    }

    [TestMethod]
    public void TheDataOutlivesTheStreamItWasReadFrom()
    {
        // A provider is promised a seekable stream and reads it during the call; what it returns must
        // therefore own fully decoded pixels rather than a reference to the stream.
        ImageData data;
        using (var stream = new MemoryStream(Png(4, 2)))
        {
            data = new ImageDataFactory().LoadFromStream(stream);
        }

        using (data)
        {
            using var bitmap = Render(data, 4, 2);
            Assert.AreEqual(SKColors.Red, bitmap.GetPixel(1, 1), "the picture survived the stream being disposed");
        }
    }

    [TestMethod]
    public void TwoLoadsOfTheSameBytesAreIndependent()
    {
        byte[] png = Png(4, 2);
        using var first = new ImageDataFactory().LoadFromBytes(png);
        using var second = new ImageDataFactory().LoadFromBytes(png);

        first.Dispose();

        using var bitmap = Render(second, 4, 2);
        Assert.AreEqual(SKColors.Red, bitmap.GetPixel(1, 1), "disposing one image leaves the other drawable");
    }

    [TestMethod]
    public void TheExtensionIsPassedToTheProviderAsAHint()
    {
        var provider = new StandIn("hinted", priority: 3, magic: "MAGIC", width: 11f);
        var factory = new ImageDataFactory([provider]);

        string path = Path.Combine(Path.GetTempPath(), $"kolvir-hint-{Guid.NewGuid():N}.weird");
        File.WriteAllBytes(path, "MAGIC"u8.ToArray());

        try
        {
            using var data = factory.LoadFromPath(path);
            Assert.AreEqual("weird", provider.LastHint, "the extension arrives without its dot");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void TheFactoryKeepsTheListItWasGiven()
    {
        // Composition, not a registry: changing the collection afterwards must not change what the
        // factory does, and two factories must not see each other's formats.
        var providers = new List<IImageFormatProvider> { new StandIn("one", priority: 3, magic: "MAGIC", width: 11f) };
        var factory = new ImageDataFactory(providers);

        providers.Add(new StandIn("two", priority: 4, magic: "MAGIC", width: 22f));

        using var data = factory.LoadFromStream(Stream("MAGIC"));
        Assert.AreEqual(11f, data.NaturalSize.Width, "the later addition to the list does not apply");
        Assert.AreEqual(1, factory.Providers.Count);
    }

    private static SKBitmap Render(ImageData data, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.DrawPicture(data.CurrentFrame!);
        return bitmap;
    }

    private static MemoryStream Stream(string magic) =>
        new([.. System.Text.Encoding.ASCII.GetBytes(magic), .. new byte[32]]);

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class TestOptions : IImageFormatOptions
    {
    }

    /// <summary>A stream that can be read once and cannot be rewound, like a pipe.</summary>
    private sealed class UnseekableStream(byte[] content) : Stream
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

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int available = Math.Min(count, content.Length - _position);
            Array.Copy(content, _position, buffer, offset, available);
            _position += available;
            return available;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A provider whose behaviour a test can read back.</summary>
    private sealed class StandIn(string name, int priority, string magic, float width) : IImageFormatProvider
    {
        internal int Creates { get; private set; }

        internal string? LastHint { get; private set; }

        internal IImageFormatOptions? LastOptions { get; private set; }

        internal bool Throws { get; init; }

        internal bool ThrowsFromCanDecode { get; init; }

        internal bool ReturnsNull { get; init; }

        public string Name => name;

        public int Priority => priority;

        public bool CanDecode(ReadOnlySpan<byte> header) =>
            ThrowsFromCanDecode
                ? throw new InvalidOperationException("this provider cannot answer questions")
                : header.StartsWith(System.Text.Encoding.ASCII.GetBytes(magic));

        public ImageData Create(Stream stream, string? extensionHint, IImageFormatOptions? options)
        {
            Creates++;
            LastHint = extensionHint;
            LastOptions = options;

            if (Throws)
            {
                throw new InvalidOperationException("this provider is broken on purpose");
            }

            if (ReturnsNull)
            {
                return null!;
            }

            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new SKRect(0f, 0f, width, width));
            using var paint = new SKPaint { Color = SKColors.Magenta };
            canvas.DrawRect(0f, 0f, width, width, paint);

            return ImageData.FromPicture(recorder.EndRecording(), disposePictureWithData: true);
        }
    }
}

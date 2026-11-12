using SkiaSharp;

namespace Kolvir.AnimatedGif.Tests;

/// <summary>
/// Animated GIF decoding: what the file says about frames, delays and looping, and what the
/// composited pixels actually are at a given position.
/// </summary>
/// <remarks>
/// <para>
/// These are data tests, not control tests: the factory and <see cref="ImageData"/> are driven
/// directly, which is how an application uses them. The control's half - the timer that repaints and
/// nothing else - is in the core suite, where the headless scene and the manual timer live.
/// </para>
/// <para>
/// Every fixture is built by <see cref="GifBuilder"/>, so the pixels, delays and disposal methods a
/// test asserts are the ones it declared rather than ones inherited from a file nobody can read.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("AnimatedGif")]
public sealed class GifDecodingTests
{
    private static readonly TimeSpan Frame0 = TimeSpan.Zero;
    private static readonly TimeSpan Frame1 = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Frame2 = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Frame3 = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan Frame4 = TimeSpan.FromMilliseconds(1000);

    private static ImageDataFactory Factory() => new([GifImageFormatProvider.Shared]);

    [TestMethod]
    public void WithoutTheFeatureAGifIsItsStillFirstFrame()
    {
        // The built-in decoder is fallback behaviour, so a GIF is never an error - it is just not an
        // animation until the package is enabled.
        using var data = new ImageDataFactory().LoadFromBytes(DisposalFile());

        Assert.AreEqual(TimeSpan.Zero, data.Duration, "a still image has no timeline");
        Assert.AreEqual(4f, data.NaturalSize.Width, "but it is the file's size");
        Assert.AreEqual(SKColors.Red, Pixel(data, Frame0, 0, 0), "and it is the first frame");
        Assert.IsFalse(data.IsPlaying, "and it never plays");
    }

    [TestMethod]
    public void RegisteringTheProviderTurnsTheSameBytesIntoAnAnimation()
    {
        using var data = Factory().LoadFromBytes(DisposalFile());

        Assert.AreEqual(TimeSpan.FromMilliseconds(1100), data.Duration, "the delays add up to the timeline");
        Assert.AreEqual(4f, data.NaturalSize.Width, "the canvas is the file's, not a frame's");
        Assert.AreEqual(4f, data.NaturalSize.Height);
    }

    [TestMethod]
    public void TheFrameIsTheOneThePositionIsInside()
    {
        using var data = Factory().LoadFromBytes(DisposalFile());

        AssertFrame(data, TimeSpan.Zero, 0);
        AssertFrame(data, TimeSpan.FromMilliseconds(99), 0);
        AssertFrame(data, Frame1, 1);
        AssertFrame(data, TimeSpan.FromMilliseconds(299), 1);
        AssertFrame(data, Frame2, 2);
        AssertFrame(data, TimeSpan.FromMilliseconds(599), 2);
        AssertFrame(data, Frame3, 3);
        AssertFrame(data, TimeSpan.FromMilliseconds(999), 3);
        AssertFrame(data, Frame4, 4);
    }

    [TestMethod]
    public void TheNextFrameComesWhenTheDelaySaysAndNotSooner()
    {
        using var data = Factory().LoadFromBytes(DisposalFile());

        // This is what keeps the control's timer waking four times a second rather than sixty.
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), data.TimeUntilNextFrame(TimeSpan.Zero));
        Assert.AreEqual(TimeSpan.FromMilliseconds(50), data.TimeUntilNextFrame(TimeSpan.FromMilliseconds(50)));
        Assert.AreEqual(TimeSpan.FromMilliseconds(200), data.TimeUntilNextFrame(Frame1));
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), data.TimeUntilNextFrame(Frame4), "the last frame waits for the pass to end");
    }

    [TestMethod]
    public void AFrameThatKeepsTheCanvasKeepsItAndBlendsItsTransparentPixel()
    {
        // Frame 1 is a 2x2 rectangle whose bottom-right pixel is the transparent index, so the red
        // underneath shows through and the other three are green. That is the blend flag doing its
        // job: a decoder that replaced the rectangle wholesale would paint transparency.
        using var data = Factory().LoadFromBytes(DisposalFile());

        using var frame = Render(data, Frame1);
        Assert.AreEqual(SKColors.Green, At(frame, 1, 1));
        Assert.AreEqual(SKColors.Green, At(frame, 2, 1));
        Assert.AreEqual(SKColors.Green, At(frame, 1, 2));
        Assert.AreEqual(SKColors.Red, At(frame, 2, 2), "the transparent pixel is not painted");
        Assert.AreEqual(SKColors.Red, At(frame, 0, 0), "and the rest of the frame is untouched");
    }

    [TestMethod]
    public void AFrameThatRestoresTheBackgroundClearsItsRectangle()
    {
        // Frame 2 covers the canvas with blue and disposes to the background, so frame 3 - a single
        // white pixel - is drawn onto a cleared canvas rather than onto blue.
        using var data = Factory().LoadFromBytes(DisposalFile());

        using var frame = Render(data, Frame3);
        Assert.AreEqual(SKColors.White, At(frame, 0, 0), "frame 3's own pixel");
        Assert.AreEqual(0, At(frame, 3, 3).Alpha, "and the blue it disposed of is gone");
    }

    [TestMethod]
    public void AFrameThatRestoresThePreviousFramePutsTheCanvasBack()
    {
        // Frame 3 disposes to "previous", so frame 4 must see the canvas as it was before frame 3
        // drew its white pixel: transparent except for frame 4's own green one.
        using var data = Factory().LoadFromBytes(DisposalFile());

        using var frame = Render(data, Frame4);
        Assert.AreEqual(SKColors.Green, At(frame, 3, 3), "frame 4's own pixel");
        Assert.AreEqual(0, At(frame, 0, 0).Alpha, "and frame 3's pixel was restored away");
    }

    [TestMethod]
    public void LoopStartsFromWhatTheFileDeclares()
    {
        using var forever = Factory().LoadFromBytes(DisposalFile(loopCount: 0));
        Assert.IsTrue(forever.Loop, "a Netscape count of zero is 'loop forever'");

        // A count of N is treated as "loops at all": the playback model has a boolean, and the count
        // is deliberately not preserved. See the README and the commit message.
        using var finite = Factory().LoadFromBytes(DisposalFile(loopCount: 3));
        Assert.IsTrue(finite.Loop, "any declared count means the file loops");

        using var none = Factory().LoadFromBytes(DisposalFile(loopCount: null));
        Assert.IsFalse(none.Loop, "a file with no loop extension plays once");

        none.Loop = true;
        Assert.IsTrue(none.Loop, "and an explicit assignment replaces what the file said");
    }

    [TestMethod]
    public void AZeroDelayIsPlayedAtTheConfiguredMinimum()
    {
        var bytes = new GifBuilder(2, 2)
            .AddFrame([GifBuilder.Red, GifBuilder.Red, GifBuilder.Red, GifBuilder.Red], delayCentiseconds: 0)
            .AddFrame([GifBuilder.Green, GifBuilder.Green, GifBuilder.Green, GifBuilder.Green], delayCentiseconds: 0)
            .ToArray();

        using var defaulted = Factory().LoadFromBytes(bytes);
        Assert.AreEqual(TimeSpan.FromMilliseconds(200), defaulted.Duration, "two zero-delay frames at the 100 ms default");

        using var fifty = new ImageDataFactory([GifImageFormatProvider.Shared])
            .LoadFromBytes(bytes, new GifOptions { ZeroDelayMinimum = TimeSpan.FromMilliseconds(50) });
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), fifty.Duration, "and the same file at a configured 50 ms");
    }

    [TestMethod]
    public void AFrameThatDeclaresADelayKeepsIt()
    {
        // Only an exact zero is replaced: a delay the author chose is not second-guessed.
        var bytes = new GifBuilder(2, 2)
            .AddFrame([GifBuilder.Red, GifBuilder.Red, GifBuilder.Red, GifBuilder.Red], delayCentiseconds: 1)
            .AddFrame([GifBuilder.Green, GifBuilder.Green, GifBuilder.Green, GifBuilder.Green], delayCentiseconds: 2)
            .ToArray();

        using var data = Factory().LoadFromBytes(bytes);
        Assert.AreEqual(TimeSpan.FromMilliseconds(30), data.Duration, "10 ms and 20 ms as declared");
    }

    [TestMethod]
    public void AnAnimationOverTheDecodedSizeLimitIsRefused()
    {
        var options = new GifOptions { DecodedSizeLimit = 64 };

        var failure = Assert.ThrowsExactly<ImageFormatException>(
            () => Factory().LoadFromBytes(DisposalFile(), options));

        Assert.AreEqual("gif", failure.ProviderName, "the provider that claimed the bytes owns the failure");
        Assert.IsInstanceOfType<InvalidOperationException>(failure.InnerException, "with the reason attached");
    }

    [TestMethod]
    public void AGifRefusesOptionsOfAnotherFormat()
    {
        // A claiming provider that is handed settings it does not understand fails rather than
        // ignoring them, because an ignored setting looks exactly like a broken one.
        var failure = Assert.ThrowsExactly<ImageFormatException>(
            () => Factory().LoadFromBytes(DisposalFile(), new ForeignOptions()));

        Assert.AreEqual("gif", failure.ProviderName);
        Assert.IsInstanceOfType<ArgumentException>(failure.InnerException);
    }

    [TestMethod]
    public void TheHighestPriorityProviderWinsAndTheLaterRegistrationBreaksATie()
    {
        var low = new StillStandIn("zeta", priority: 60);
        var high = new StillStandIn("alpha", priority: 70);
        var factory = new ImageDataFactory([high, low, GifImageFormatProvider.Shared]);

        using var higher = factory.LoadFromBytes(DisposalFile());
        Assert.AreEqual(11f, higher.NaturalSize.Width, "the higher priority provider produced the data");
        Assert.AreEqual(0, low.Created, "and the other one was never asked to decode");

        // Equal priority is decided by registration order, the later one winning, so a package can
        // be replaced by registering another provider at the same priority.
        var first = new StillStandIn("aaa", priority: 80);
        var second = new StillStandIn("bbb", priority: 80);
        var ordered = new ImageDataFactory([first, second, GifImageFormatProvider.Shared]);

        using var equal = ordered.LoadFromBytes(DisposalFile());
        Assert.AreEqual(11f, equal.NaturalSize.Width, "the later registration wins at equal priority");
        Assert.AreEqual(1, second.Created, "it is the one that produced the data");
        Assert.AreEqual(0, first.Created, "and the one it shadowed was never asked to decode");
    }

    [TestMethod]
    public void AProviderThatThrowsWhileSniffingIsSkipped()
    {
        var broken = new StillStandIn("broken", priority: 100) { ThrowsFromCanDecode = true };
        var factory = new ImageDataFactory([broken, GifImageFormatProvider.Shared]);

        using var data = factory.LoadFromBytes(DisposalFile());
        Assert.AreEqual(TimeSpan.FromMilliseconds(1100), data.Duration, "the GIF provider still claimed the bytes");
        Assert.AreEqual(0, broken.Created, "and the broken one was never asked to decode");
    }

    private static void AssertFrame(ImageData data, TimeSpan position, int expected)
    {
        using var frame = Render(data, position);
        var expectedColour = expected switch
        {
            0 => SKColors.Red,
            1 => SKColors.Green,
            2 => SKColors.Blue,
            3 => SKColors.White,
            _ => SKColors.Green,
        };

        var pixel = expected switch
        {
            1 => (1, 1),
            2 => (0, 0),
            3 => (0, 0),
            4 => (3, 3),
            _ => (0, 0),
        };

        Assert.AreEqual(
            expectedColour,
            At(frame, pixel.Item1, pixel.Item2),
            $"position {position} should show frame {expected}");
    }

    private static SKBitmap Render(ImageData data, TimeSpan position)
    {
        data.Seek(position);

        var bitmap = new SKBitmap(4, 4);
        using var canvas = new SKCanvas(bitmap);
        canvas.DrawPicture(data.CurrentFrame!);
        return bitmap;
    }

    private static SKColor Pixel(ImageData data, TimeSpan position, int x, int y)
    {
        using var bitmap = Render(data, position);
        return At(bitmap, x, y);
    }

    private static SKColor At(SKBitmap bitmap, int x, int y) => bitmap.GetPixel(x, y);

    /// <summary>
    /// 4x4, five frames: red; a 2x2 green rectangle with one transparent pixel; blue (background
    /// disposal); a white pixel (previous disposal); a green pixel.
    /// </summary>
    private static byte[] DisposalFile(int? loopCount = 0) =>
        new GifBuilder(4, 4, loopCount)
            .AddFrame(
                [
                    GifBuilder.Red, GifBuilder.Red, GifBuilder.Red, GifBuilder.Red,
                    GifBuilder.Red, GifBuilder.Red, GifBuilder.Red, GifBuilder.Red,
                    GifBuilder.Red, GifBuilder.Red, GifBuilder.Red, GifBuilder.Red,
                    GifBuilder.Red, GifBuilder.Red, GifBuilder.Red, GifBuilder.Red,
                ],
                delayCentiseconds: 10,
                disposal: 1)
            .AddFrame(
                1,
                1,
                2,
                2,
                [
                    GifBuilder.Green, GifBuilder.Green,
                    GifBuilder.Green, GifBuilder.Transparent,
                ],
                delayCentiseconds: 20,
                disposal: 1,
                transparent: true)
            .AddFrame(
                [
                    GifBuilder.Blue, GifBuilder.Blue, GifBuilder.Blue, GifBuilder.Blue,
                    GifBuilder.Blue, GifBuilder.Blue, GifBuilder.Blue, GifBuilder.Blue,
                    GifBuilder.Blue, GifBuilder.Blue, GifBuilder.Blue, GifBuilder.Blue,
                    GifBuilder.Blue, GifBuilder.Blue, GifBuilder.Blue, GifBuilder.Blue,
                ],
                delayCentiseconds: 30,
                disposal: 2)
            .AddFrame(0, 0, 1, 1, [GifBuilder.White], delayCentiseconds: 40, disposal: 3)
            .AddFrame(3, 3, 1, 1, [GifBuilder.Green], delayCentiseconds: 10, disposal: 1)
            .ToArray();

    private sealed class ForeignOptions : IImageFormatOptions
    {
    }

    /// <summary>A provider that claims GIF bytes and produces a still of a different size.</summary>
    private sealed class StillStandIn(string name, int priority) : IImageFormatProvider
    {
        internal int Created { get; private set; }

        internal bool ThrowsFromCanDecode { get; init; }

        public string Name => name;

        public int Priority => priority;

        public bool CanDecode(ReadOnlySpan<byte> header) =>
            ThrowsFromCanDecode
                ? throw new InvalidOperationException("this provider cannot answer questions")
                : header.Length >= 6 && header[..3].SequenceEqual("GIF"u8);

        public ImageData Create(Stream stream, string? extensionHint, IImageFormatOptions? options)
        {
            Created++;

            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new SKRect(0f, 0f, 11f, 11f));
            using var paint = new SKPaint { Color = SKColors.Magenta };
            canvas.DrawRect(0f, 0f, 11f, 11f, paint);
            return ImageData.FromPicture(recorder.EndRecording(), disposePictureWithData: true);
        }
    }
}

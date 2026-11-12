using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Kolvir.AnimatedWebp.Tests;

/// <summary>
/// Animated WebP decoding: what the container says about frames, durations and looping, and what the
/// composited pixels actually are at a given position.
/// </summary>
/// <remarks>
/// <para>
/// These are data tests, not control tests: the factory and <see cref="ImageData"/> are driven
/// directly, which is how an application uses them. The control's half - the timer that repaints and
/// nothing else - is in the core suite, where the headless scene and the manual timer live, and it is
/// shared with GIF because the timeline is one implementation.
/// </para>
/// <para>
/// Every fixture is built by <see cref="WebpBuilder"/>, so the pixels, durations, blend bits and
/// disposal bits a test asserts are the ones it declared rather than ones inherited from a file
/// nobody can read. Lossless encoding keeps the colours exact.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("AnimatedWebp")]
public sealed class WebpDecodingTests
{
    private static readonly TimeSpan Frame0 = TimeSpan.Zero;
    private static readonly TimeSpan Frame1 = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Frame2 = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Frame3 = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan Frame4 = TimeSpan.FromMilliseconds(1000);

    private static ImageDataFactory Factory() => new([WebpImageFormatProvider.Shared]);

    [TestMethod]
    public void WithoutTheFeatureAWebpIsItsStillFirstFrame()
    {
        // The built-in decoder reads WebP, so a WebP is never an error - it is just not an
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

        Assert.AreEqual(TimeSpan.FromMilliseconds(1500), data.Duration, "the durations add up to the timeline");
        Assert.AreEqual(4f, data.NaturalSize.Width, "the canvas is the file's, not a frame's");
        Assert.AreEqual(4f, data.NaturalSize.Height);
        Assert.AreEqual(5, FrameStarts(data).Count, "one boundary per ANMF");
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
    public void TheNextFrameComesWhenTheDurationSaysAndNotSooner()
    {
        using var data = Factory().LoadFromBytes(DisposalFile());

        // This is what keeps the control's timer waking four times a second rather than sixty.
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), data.TimeUntilNextFrame(TimeSpan.Zero));
        Assert.AreEqual(TimeSpan.FromMilliseconds(50), data.TimeUntilNextFrame(TimeSpan.FromMilliseconds(50)));
        Assert.AreEqual(TimeSpan.FromMilliseconds(200), data.TimeUntilNextFrame(Frame1));
        Assert.AreEqual(TimeSpan.FromMilliseconds(500), data.TimeUntilNextFrame(Frame4), "the last frame waits for the pass to end");
    }

    [TestMethod]
    public void ABlendingFrameLetsTheCanvasShowThroughItsTransparentPixel()
    {
        // Frame 1 is a 2x2 rectangle whose bottom-right pixel is transparent and whose blend bit is
        // clear, so it is drawn over the red underneath: three green pixels and the red showing
        // through. A decoder that replaced the rectangle wholesale would paint red there as well, but
        // one that blurred the source and destination would not produce these exact colours.
        using var data = Factory().LoadFromBytes(DisposalFile());

        using var frame = Render(data, Frame1);
        Assert.AreEqual(SKColors.Green, At(frame, 0, 0));
        Assert.AreEqual(SKColors.Green, At(frame, 1, 0));
        Assert.AreEqual(SKColors.Green, At(frame, 0, 1));
        Assert.AreEqual(SKColors.Red, At(frame, 1, 1), "the transparent pixel is not painted");
        Assert.AreEqual(SKColors.Red, At(frame, 3, 3), "and the rest of the canvas is untouched");
    }

    [TestMethod]
    public void ADoNotBlendFrameReplacesItsRectangleIncludingTransparency()
    {
        // Frame 2's blend bit is set - "do not blend" in the container's words - so its rectangle
        // replaces what is underneath, transparent pixel included. That is the opposite of frame 1,
        // and a compositor that treated the bit as GIF's blend flag would leave red at (3,3).
        using var data = Factory().LoadFromBytes(DisposalFile());

        using var frame = Render(data, Frame2);
        Assert.AreEqual(SKColors.Blue, At(frame, 2, 2), "the rectangle is drawn");
        Assert.AreEqual(SKColors.Blue, At(frame, 3, 2));
        Assert.AreEqual(SKColors.Blue, At(frame, 2, 3));
        Assert.AreEqual(0, At(frame, 3, 3).Alpha, "and its transparent pixel replaced the old red");
        Assert.AreEqual(SKColors.Green, At(frame, 0, 0), "the previous frame outside its rectangle is kept");
    }

    [TestMethod]
    public void ALossyFrameWithASeparateAlphaChunkIsComposited()
    {
        // A lossy VP8 frame carries its alpha in a separate ALPH chunk, which the compositor has to
        // put into a VP8X container before Skia will decode it. Lossless fixtures never take that
        // branch, so this one is encoded lossily on purpose; the colour is approximate and the alpha
        // is exact, which is what a lossy frame promises.
        var bytes = new WebpBuilder(4, 4)
            .AddFrame(Fill(4, 4, SKColors.Red), durationMilliseconds: 100)
            .AddFrame(
                0,
                0,
                2,
                2,
                [SKColors.Green, SKColors.Green, SKColors.Green, SKColors.Transparent],
                durationMilliseconds: 100,
                blend: false,
                lossy: true)
            .ToArray();

        using var data = Factory().LoadFromBytes(bytes);
        using var frame = Render(data, TimeSpan.FromMilliseconds(150));

        var green = At(frame, 0, 0);
        Assert.AreEqual(255, green.Alpha, "the opaque pixel is opaque");
        Assert.IsTrue(green.Green > 100 && green.Red < 40, $"the ALPH and VP8 chunks decoded as green, was {green}");
        Assert.AreEqual(0, At(frame, 1, 1).Alpha, "and the ALPH chunk's transparent pixel replaced the red");
    }

    [TestMethod]
    public void AFrameThatDisposesToBackgroundClearsItsRectangle()
    {
        // Frame 3 covers the canvas with blue and disposes to the background, so frame 4 - a single
        // white pixel - is drawn onto a cleared canvas rather than onto blue.
        using var data = Factory().LoadFromBytes(DisposalFile());

        using var frame = Render(data, Frame4);
        Assert.AreEqual(SKColors.White, At(frame, 0, 0), "frame 4's own pixel");
        Assert.AreEqual(0, At(frame, 3, 3).Alpha, "and the blue it disposed of is gone");
        Assert.AreEqual(0, At(frame, 1, 1).Alpha, "including the green from two frames earlier");
    }

    [TestMethod]
    public void SubRectangleFramesAreCompositedOntoTheFullCanvas()
    {
        var bytes = new WebpBuilder(8, 6)
            .AddFrame(0, 0, 8, 6, Fill(8, 6, SKColors.Red), durationMilliseconds: 100)
            .AddFrame(2, 2, 3, 2, Fill(3, 2, SKColors.Green), durationMilliseconds: 100)
            .ToArray();

        using var data = Factory().LoadFromBytes(bytes);

        Assert.AreEqual(8f, data.NaturalSize.Width, "the picture is the canvas, not the frame");
        Assert.AreEqual(6f, data.NaturalSize.Height);

        using var frame = Render(data, TimeSpan.FromMilliseconds(150), 8, 6);
        Assert.AreEqual(SKColors.Green, At(frame, 2, 2), "the sub-rectangle lands at its offset");
        Assert.AreEqual(SKColors.Green, At(frame, 4, 3));
        Assert.AreEqual(SKColors.Red, At(frame, 1, 1), "and the canvas around it is untouched");
        Assert.AreEqual(SKColors.Red, At(frame, 5, 4));
        Assert.AreEqual(SKColors.Red, At(frame, 0, 0));
    }

    [TestMethod]
    public void LoopStartsFromWhatTheFileDeclares()
    {
        // A loop count of zero is "forever", and any other count is "play N times". The playback
        // model has a boolean and does not preserve N, so both start Loop true, exactly as the GIF
        // feature decides for the Netscape extension. See the README and the GIF feature's remarks.
        using var forever = Factory().LoadFromBytes(DisposalFile(loopCount: 0));
        Assert.IsTrue(forever.Loop, "a count of zero is 'loop forever'");

        using var finite = Factory().LoadFromBytes(DisposalFile(loopCount: 3));
        Assert.IsTrue(finite.Loop, "any declared count means the file loops");

        using var once = Factory().LoadFromBytes(DisposalFile(loopCount: 1));
        Assert.IsTrue(once.Loop, "even a count of one is a declaration, and the count is not preserved");

        using var undeclared = Factory().LoadFromBytes(DisposalFile(loopCount: null));
        Assert.IsFalse(undeclared.Loop, "an animation with no ANIM chunk plays once");

        using var still = Factory().LoadFromBytes(WebpBuilder.EncodeStill(2, 2, (_, _) => SKColors.Red));
        Assert.IsFalse(still.Loop, "and a still picture never loops");

        finite.Loop = true;
        Assert.IsTrue(finite.Loop, "an explicit assignment replaces what the file said");
    }

    [TestMethod]
    public void AZeroDurationIsPlayedAtTheConfiguredMinimum()
    {
        var bytes = new WebpBuilder(2, 2)
            .AddFrame(Fill(2, 2, SKColors.Red), durationMilliseconds: 0)
            .AddFrame(Fill(2, 2, SKColors.Green), durationMilliseconds: 0)
            .ToArray();

        using var defaulted = Factory().LoadFromBytes(bytes);
        Assert.AreEqual(TimeSpan.FromMilliseconds(200), defaulted.Duration, "two zero-duration frames at the 100 ms default");

        using var fifty = Factory().LoadFromBytes(bytes, new WebpOptions { ZeroDelayMinimum = TimeSpan.FromMilliseconds(50) });
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), fifty.Duration, "and the same file at a configured 50 ms");
    }

    [TestMethod]
    public void AFrameThatDeclaresADurationKeepsIt()
    {
        // Only an exact zero is replaced: a duration the author chose is not second-guessed.
        var bytes = new WebpBuilder(2, 2)
            .AddFrame(Fill(2, 2, SKColors.Red), durationMilliseconds: 10)
            .AddFrame(Fill(2, 2, SKColors.Green), durationMilliseconds: 20)
            .ToArray();

        using var data = Factory().LoadFromBytes(bytes);
        Assert.AreEqual(TimeSpan.FromMilliseconds(30), data.Duration, "10 ms and 20 ms as declared");
    }

    [TestMethod]
    public void AnAnimationOverTheDecodedSizeLimitIsRefused()
    {
        var options = new WebpOptions { DecodedSizeLimit = 64 };

        var failure = Assert.ThrowsExactly<ImageFormatException>(
            () => Factory().LoadFromBytes(DisposalFile(), options));

        Assert.AreEqual("webp", failure.ProviderName, "the provider that claimed the bytes owns the failure");
        Assert.IsInstanceOfType<InvalidOperationException>(failure.InnerException, "with the reason attached");
    }

    [TestMethod]
    public void AWebpRefusesOptionsOfAnotherFormat()
    {
        // A claiming provider that is handed settings it does not understand fails rather than
        // ignoring them, because an ignored setting looks exactly like a broken one.
        var failure = Assert.ThrowsExactly<ImageFormatException>(
            () => Factory().LoadFromBytes(DisposalFile(), new ForeignOptions()));

        Assert.AreEqual("webp", failure.ProviderName);
        Assert.IsInstanceOfType<ArgumentException>(failure.InnerException);
    }

    [TestMethod]
    public void AStaticWebpIsTheSamePictureThroughTheFeatureAndTheBuiltInDecoder()
    {
        var bytes = WebpBuilder.EncodeStill(
            4,
            4,
            (x, y) => (x < 2, y < 2) switch
            {
                (true, true) => SKColors.Red,
                (false, true) => SKColors.Green,
                (true, false) => SKColors.Blue,
                _ => SKColors.White,
            });

        using var builtIn = new ImageDataFactory().LoadFromBytes(bytes);
        using var feature = Factory().LoadFromBytes(bytes);

        Assert.AreEqual(TimeSpan.Zero, feature.Duration, "a still has no timeline through either path");
        Assert.IsFalse(feature.Loop, "and nothing loops");
        Assert.AreEqual(4f, feature.NaturalSize.Width);

        using var a = Render(builtIn, TimeSpan.Zero);
        using var b = Render(feature, TimeSpan.Zero);

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Assert.AreEqual(
                    At(a, x, y),
                    At(b, x, y),
                    $"enabling the feature must not change the still pixel at ({x},{y})");
            }
        }
    }

    [TestMethod]
    public void ASingleFrameWebpIsAStillPicture()
    {
        // The GIF feature treats a single-frame GIF as a still, with no timeline and no loop. A
        // one-frame ANMF is the WebP spelling of the same file, so it gets the same answer.
        var animated = new WebpBuilder(4, 4, loopCount: 0)
            .AddFrame(2, 2, 2, 2, Fill(2, 2, SKColors.Green), durationMilliseconds: 500)
            .ToArray();

        using var data = Factory().LoadFromBytes(animated);

        Assert.AreEqual(TimeSpan.Zero, data.Duration, "one frame is not a timeline");
        Assert.IsFalse(data.Loop, "and it does not claim to repeat");
        Assert.IsFalse(data.IsPlaying, "so it never plays");
        Assert.AreEqual(4f, data.NaturalSize.Width, "but the canvas is still the canvas");
        Assert.AreEqual(SKColors.Green, Pixel(data, TimeSpan.Zero, 2, 2), "and its rectangle is composited");
        Assert.AreEqual(0, Pixel(data, TimeSpan.Zero, 0, 0).Alpha, "onto a transparent canvas");
    }

    [TestMethod]
    public void TheProviderClaimsWebpAndLeavesOtherFormatsAlone()
    {
        var provider = WebpImageFormatProvider.Shared;

        Assert.AreEqual("webp", provider.Name);
        Assert.AreEqual(50, provider.Priority);
        Assert.IsTrue(provider.CanDecode(DisposalFile()), "an animated WebP is claimed");
        Assert.IsTrue(
            provider.CanDecode(WebpBuilder.EncodeStill(2, 2, (_, _) => SKColors.Red)),
            "a static WebP is claimed too");

        var png = PngFixture();
        var jpeg = JpegFixture();
        Assert.IsFalse(provider.CanDecode(png), "PNG is not WebP");
        Assert.IsFalse(provider.CanDecode(jpeg), "JPEG is not WebP");
        Assert.IsFalse(provider.CanDecode("GIF89a"u8), "GIF is not WebP");
    }

    [TestMethod]
    public void RegisteringTheProviderDoesNotChangePngOrJpeg()
    {
        var png = PngFixture();
        var jpeg = JpegFixture();

        using var pngData = Factory().LoadFromBytes(png);
        Assert.AreEqual(TimeSpan.Zero, pngData.Duration, "PNG is still a still picture");
        Assert.AreEqual(6f, pngData.NaturalSize.Width);
        Assert.AreEqual(SKColors.Red, Pixel(pngData, TimeSpan.Zero, 1, 1));

        using var jpegData = Factory().LoadFromBytes(jpeg);
        Assert.AreEqual(TimeSpan.Zero, jpegData.Duration, "JPEG is still a still picture");
        Assert.AreEqual(6f, jpegData.NaturalSize.Width);

        var jpegPixel = Pixel(jpegData, TimeSpan.Zero, 1, 1);
        Assert.IsTrue(jpegPixel.Red > 200 && jpegPixel.Green < 60 && jpegPixel.Blue < 60, $"JPEG decoded as red, was {jpegPixel}");
    }

    [TestMethod]
    public void TheServiceCollectionRegistrationAddsTheSameProvider()
    {
        var services = new ServiceCollection();
        var returned = services.AddAnimatedWebpImages();

        Assert.AreSame(services, returned, "the registration chains");

        var descriptor = services.Single(entry => entry.ServiceType == typeof(IImageFormatProvider));
        Assert.AreSame(WebpImageFormatProvider.Shared, descriptor.ImplementationInstance);
    }

    private static void AssertFrame(ImageData data, TimeSpan position, int expected)
    {
        using var frame = Render(data, position);
        var expectedColour = expected switch
        {
            0 => SKColors.Red,
            1 => SKColors.Green,
            2 => SKColors.Blue,
            3 => SKColors.Blue,
            _ => SKColors.White,
        };

        var pixel = expected switch
        {
            1 => (0, 0),
            2 => (2, 2),
            3 => (0, 0),
            4 => (0, 0),
            _ => (0, 0),
        };

        Assert.AreEqual(
            expectedColour,
            At(frame, pixel.Item1, pixel.Item2),
            $"position {position} should show frame {expected}");
    }

    /// <summary>The frame starts the timeline produces, walked through the boundaries it reports.</summary>
    private static List<TimeSpan> FrameStarts(ImageData data)
    {
        var starts = new List<TimeSpan> { TimeSpan.Zero };
        var position = TimeSpan.Zero;

        while (true)
        {
            var wait = data.TimeUntilNextFrame(position);
            if (wait is not { } next || next <= TimeSpan.Zero)
            {
                break;
            }

            position += next;
            if (position >= data.Duration)
            {
                break;
            }

            starts.Add(position);
        }

        return starts;
    }

    private static SKBitmap Render(ImageData data, TimeSpan position, int width = 4, int height = 4)
    {
        data.Seek(position);

        var bitmap = new SKBitmap(width, height);
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

    private static SKColor[] Fill(int width, int height, SKColor colour) =>
        [.. Enumerable.Repeat(colour, width * height)];

    private static byte[] PngFixture()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(6, 6, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static byte[] JpegFixture()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(6, 6, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        return encoded.ToArray();
    }

    /// <summary>
    /// 4x4, five frames: red; a 2x2 green rectangle with one transparent pixel (blend); a 2x2 blue
    /// rectangle with one transparent pixel (do not blend); blue with background disposal; a white
    /// pixel.
    /// </summary>
    private static byte[] DisposalFile(int? loopCount = 0) =>
        new WebpBuilder(4, 4, loopCount)
            .AddFrame(Fill(4, 4, SKColors.Red), durationMilliseconds: 100)
            .AddFrame(
                0,
                0,
                2,
                2,
                [SKColors.Green, SKColors.Green, SKColors.Green, SKColors.Transparent],
                durationMilliseconds: 200,
                blend: true)
            .AddFrame(
                2,
                2,
                2,
                2,
                [SKColors.Blue, SKColors.Blue, SKColors.Blue, SKColors.Transparent],
                durationMilliseconds: 300,
                blend: false)
            .AddFrame(Fill(4, 4, SKColors.Blue), durationMilliseconds: 400, disposeToBackground: true)
            .AddFrame(0, 0, 1, 1, [SKColors.White], durationMilliseconds: 500)
            .ToArray();

    private sealed class ForeignOptions : IImageFormatOptions
    {
    }
}

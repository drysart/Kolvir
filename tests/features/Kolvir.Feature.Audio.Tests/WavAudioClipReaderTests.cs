using Kolvir.Audio;

namespace Kolvir.Audio.Tests;

/// <summary>
/// What the WAV reader accepts, what it converts on the way in, and what it refuses.
/// </summary>
/// <remarks>
/// The conversion to 16-bit PCM is the contract the platform halves rely on: they open a stream with
/// one format, so a reader that quietly left a file at its own width would produce a stream that plays
/// at the wrong speed on two of the three platforms. The refusals matter for the opposite reason -
/// "no audio device" must not become "no idea whether this file was any good".
/// </remarks>
[TestClass]
public sealed class WavAudioClipReaderTests
{
    private AudioEngine _engine = null!;

    [TestInitialize]
    public void Setup() => _engine = new AudioEngine(platform: null);

    [TestCleanup]
    public void Cleanup() => _engine.Dispose();

    [TestMethod]
    public void PcmSixteenKeepsItsSamplesAndItsLength()
    {
        short[] samples = [0, 1000, -1000, 32767, -32768, 5];
        var clip = _engine.Load(new MemoryStream(Wav.Pcm(samples, sampleRate: 8000, channels: 1)));

        Assert.AreEqual(8000, clip.SampleRate);
        Assert.AreEqual(1, clip.Channels);
        CollectionAssert.AreEqual(samples, Enumerable.Range(0, samples.Length).Select(i => Wav.SampleAt(clip, i)).ToArray());
    }

    [TestMethod]
    public void DurationIsFramesOverRate()
    {
        // 800 frames at 8000 Hz is a tenth of a second, counted as frames rather than as samples so a
        // stereo file is not twice as long as the same sound in mono.
        var clip = _engine.Load(new MemoryStream(Wav.Pcm(new short[1600], sampleRate: 8000, channels: 2)));

        Assert.AreEqual(TimeSpan.FromMilliseconds(100), clip.Duration);
    }

    [TestMethod]
    public void EightBitSamplesAreUnsignedAndAreWidened()
    {
        // 8-bit WAV is unsigned with 128 as silence; the two ends of the range have to land on the two
        // ends of the 16-bit range, not in the middle of it.
        var file = WithData([128, 255, 0, 129], bitsPerSample: 8);
        var clip = _engine.Load(new MemoryStream(file));

        Assert.AreEqual(0, Wav.SampleAt(clip, 0), "128 is silence");
        Assert.AreEqual(32512, Wav.SampleAt(clip, 1), "255 is just under full scale");
        Assert.AreEqual(-32768, Wav.SampleAt(clip, 2), "0 is the bottom of the range");
        Assert.AreEqual(256, Wav.SampleAt(clip, 3));
    }

    [TestMethod]
    public void TwentyFourBitSamplesKeepTheirSign()
    {
        // -1 in 24-bit is FF FF FF: converting it as if it were unsigned is the classic way to turn a
        // quiet sample into the loudest one in the file. The second sample is zero, and the third is
        // 0x010000 - a small positive value whose top byte has a zero sign bit.
        var file = WithData([0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01], bitsPerSample: 24);
        var clip = _engine.Load(new MemoryStream(file));

        Assert.IsTrue(Wav.SampleAt(clip, 0) < 0, "a negative 24-bit sample stays negative");
        Assert.AreEqual(0, Wav.SampleAt(clip, 1));
        Assert.AreEqual(256, Wav.SampleAt(clip, 2), "the top sixteen of the twenty-four bits are kept");
    }

    [TestMethod]
    public void FloatSamplesAreClampedRatherThanWrapped()
    {
        var file = WithFloat([1.5f, -1.5f, 0.5f, float.NaN]);
        var clip = _engine.Load(new MemoryStream(file));

        Assert.AreEqual(short.MaxValue, Wav.SampleAt(clip, 0));
        Assert.AreEqual(short.MinValue, Wav.SampleAt(clip, 1));
        Assert.AreEqual(16383, Wav.SampleAt(clip, 2));
        Assert.AreEqual(0, Wav.SampleAt(clip, 3), "a NaN sample is silence, not a click");
    }

    [TestMethod]
    public void TheExtensibleFormIsReadThroughItsSubFormat()
    {
        var clip = _engine.Load(new MemoryStream(Wav.Extensible([100, -100])));

        Assert.AreEqual(100, Wav.SampleAt(clip, 0));
        Assert.AreEqual(-100, Wav.SampleAt(clip, 1));
    }

    [TestMethod]
    public void AnOddSizedChunkBeforeTheAudioIsSteppedOver()
    {
        var clip = _engine.Load(new MemoryStream(Wav.OddSizedChunkBeforeData([7, 8])));

        Assert.AreEqual(2, clip.Frames);
        Assert.AreEqual(7, Wav.SampleAt(clip, 0));
    }

    [TestMethod]
    public void ACompressedFormatIsRefused()
    {
        // 0x55 is MP3 inside a WAV container, which is exactly the sort of file a caller will hand a
        // "WAV reader" and expect to work.
        Assert.ThrowsExactly<NotSupportedException>(() => _engine.Load(new MemoryStream(Wav.WithFormatTag(0x55))));
    }

    [TestMethod]
    public void SomethingThatIsNotRiffIsRefused()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => _engine.Load(new MemoryStream(Wav.NotRiff())));
    }

    [TestMethod]
    public void AFileWithNoFormatChunkIsRefused()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => _engine.Load(new MemoryStream(Wav.DataBeforeFormat())));
    }

    [TestMethod]
    public void ATailThatIsNotAWholeFrameIsDropped()
    {
        // One and a half frames: the half frame is not a sample and must not become a click.
        var file = WithData([0, 0, 0, 0, 0x7F], bitsPerSample: 16);
        var clip = _engine.Load(new MemoryStream(file));

        Assert.AreEqual(2, clip.Frames);
    }

    /// <summary>A mono file whose data chunk is written as raw bytes, for widths a <c>short</c> cannot express.</summary>
    private static byte[] WithData(byte[] data, int bitsPerSample) => Wav.FromRaw(data, bitsPerSample);

    private static byte[] WithFloat(float[] samples)
    {
        var bytes = new byte[samples.Length * 4];

        for (int i = 0; i < samples.Length; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4, 4), samples[i]);
        }

        return Wav.FromRaw(bytes, bitsPerSample: 32, formatTag: 3);
    }
}

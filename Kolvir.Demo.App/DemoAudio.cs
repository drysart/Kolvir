using System.Buffers.Binary;
using System.Text;

namespace Kolvir.Demo;

/// <summary>
/// The sounds the audio page plays, built as WAV files in memory.
/// </summary>
/// <remarks>
/// <para>
/// The page needs something to play and the repository carries no audio assets, so the sounds are
/// generated: a tone, a sweep and a click are three lines of arithmetic, and a generated sound also
/// proves the WAV reader against a file whose every byte is known rather than against whatever a
/// sound editor wrote.
/// </para>
/// <para>
/// All of them are 44.1 kHz, stereo and 16-bit, which is what an application shipping a real sound
/// effect would use and what the decoder normalises everything to.
/// </para>
/// </remarks>
internal static class DemoAudio
{
    private const int Rate = 44100;
    private const int Channels = 2;

    /// <summary>A steady sine at <paramref name="frequency"/>, with a short fade at each end.</summary>
    /// <remarks>
    /// The fades matter: a sine that starts and stops at full amplitude clicks, and a click is the one
    /// thing that makes a "smooth" sound effect sound broken.
    /// </remarks>
    public static byte[] Tone(double frequency = 440, double seconds = 1.2, float amplitude = 0.25f)
    {
        int frames = (int)(Rate * seconds);
        var samples = new double[frames];

        for (int i = 0; i < frames; i++)
        {
            samples[i] = Math.Sin(2 * Math.PI * frequency * ((double)i / Rate)) * amplitude;
        }

        Fade(samples);
        return Encode(samples);
    }

    /// <summary>
    /// A glide from one frequency to another, which is what makes a latency problem audible: a steady
    /// tone sounds the same when a frame is dropped, while a sweep does not.
    /// </summary>
    public static byte[] Sweep(double from = 220, double to = 1760, double seconds = 1.5, float amplitude = 0.22f)
    {
        int frames = (int)(Rate * seconds);
        var samples = new double[frames];

        // Phase is accumulated rather than recomputed from the instantaneous frequency, because a
        // frequency that changes per sample would otherwise discontinuously reset the waveform.
        double phase = 0;

        for (int i = 0; i < frames; i++)
        {
            double t = (double)i / frames;
            double frequency = from * Math.Pow(to / from, t);

            phase += 2 * Math.PI * frequency / Rate;
            samples[i] = Math.Sin(phase) * amplitude * (1 - (t * 0.35));
        }

        Fade(samples);
        return Encode(samples);
    }

    /// <summary>
    /// A short percussive click: a burst of a high tone under a fast decay.
    /// </summary>
    /// <remarks>
    /// What an application actually plays most often is a click, and it is the sound most likely to
    /// expose a late start, because it is over before a slow path has finished opening a stream.
    /// </remarks>
    public static byte[] Click(double frequency = 1320, double seconds = 0.09, float amplitude = 0.3f)
    {
        int frames = (int)(Rate * seconds);
        var samples = new double[frames];

        for (int i = 0; i < frames; i++)
        {
            double t = (double)i / Rate;
            double decay = Math.Exp(-t * 55);

            samples[i] = Math.Sin(2 * Math.PI * frequency * t) * amplitude * decay;
        }

        Fade(samples, fadeSeconds: 0.004);
        return Encode(samples);
    }

    /// <summary>Silences the first and last few milliseconds, so nothing starts or stops mid-waveform.</summary>
    private static void Fade(double[] samples, double fadeSeconds = 0.012)
    {
        int fade = Math.Min(samples.Length / 2, (int)(Rate * fadeSeconds));

        for (int i = 0; i < fade; i++)
        {
            double gain = (double)i / fade;
            samples[i] *= gain;
            samples[samples.Length - 1 - i] *= gain;
        }
    }

    /// <summary>Wraps samples in a canonical 16-bit stereo WAV, which is what the decoder reads.</summary>
    private static byte[] Encode(double[] samples)
    {
        int dataBytes = samples.Length * Channels * 2;
        var wav = new byte[44 + dataBytes];

        void Tag(int offset, string text) => Encoding.ASCII.GetBytes(text).CopyTo(wav, offset);
        void Int32(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(offset), value);
        void Int16(int offset, short value) => BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(offset), value);

        Tag(0, "RIFF");
        Int32(4, wav.Length - 8);
        Tag(8, "WAVE");
        Tag(12, "fmt ");
        Int32(16, 16);
        Int16(20, 1);
        Int16(22, Channels);
        Int32(24, Rate);
        Int32(28, Rate * Channels * 2);
        Int16(32, Channels * 2);
        Int16(34, 16);
        Tag(36, "data");
        Int32(40, dataBytes);

        for (int i = 0; i < samples.Length; i++)
        {
            short value = (short)Math.Clamp(samples[i] * short.MaxValue, short.MinValue, short.MaxValue);
            int offset = 44 + (i * 4);

            Int16(offset, value);
            Int16(offset + 2, value);
        }

        return wav;
    }
}

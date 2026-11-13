using System.Buffers.Binary;
using System.Text;

namespace Kolvir.Audio.Tests;

/// <summary>
/// Builds WAV files byte by byte, so a test can say exactly what it means rather than shipping a
/// binary it cannot read.
/// </summary>
internal static class Wav
{
    /// <summary>A canonical 16-bit PCM file: the shape every other reader in the world agrees on.</summary>
    public static byte[] Pcm(short[] samples, int sampleRate = 8000, int channels = 1, int bitsPerSample = 16) =>
        Build(samples.Length * (bitsPerSample / 8), sampleRate, channels, bitsPerSample, formatTag: 1, samples);

    /// <summary>A file whose format chunk is the extensible form wrapping a plain PCM tag.</summary>
    public static byte[] Extensible(short[] samples, int sampleRate = 8000, int channels = 1) =>
        Build(
            samples.Length * 2,
            sampleRate,
            channels,
            16,
            formatTag: 0xFFFE,
            sampleWriter: stream => WriteSamples(stream, samples, 16),
            extensible: true);

    /// <summary>A file in a format this reader must refuse, with <paramref name="formatTag"/> in the header.</summary>
    public static byte[] WithFormatTag(ushort formatTag) =>
        Build(4, 8000, 1, 4, formatTag, sampleWriter: stream => stream.Write(new byte[4]));

    /// <summary>A mono file whose data chunk is exactly <paramref name="data"/>, for any sample width.</summary>
    public static byte[] FromRaw(byte[] data, int bitsPerSample, ushort formatTag = 1) =>
        Build(data.Length, 8000, 1, bitsPerSample, formatTag, sampleWriter: stream => stream.Write(data));

    /// <summary>Raw bytes with no WAVE header at all.</summary>
    public static byte[] NotRiff() => Encoding.ASCII.GetBytes("this is not a sound file, it is a sentence.");

    /// <summary>A file whose data chunk arrives before its format chunk.</summary>
    public static byte[] DataBeforeFormat()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(0);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        WriteChunk(writer, "data", 2, inner => inner.Write(new byte[2]));
        WriteChunk(writer, "fmt ", 16, inner => WriteFormat(inner, 8000, 1, 16, 1, extensible: false));
        Finish(writer);

        return stream.ToArray();
    }

    /// <summary>
    /// A file with an odd-sized chunk before the audio, which is the padding byte every chunk walker
    /// has to step over.
    /// </summary>
    public static byte[] OddSizedChunkBeforeData(short[] samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(0);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        WriteChunk(writer, "fmt ", 16, inner => WriteFormat(inner, 8000, 1, 16, 1, extensible: false));

        // A 'LIST' chunk of three bytes: the payload, then one padding byte that is not part of it.
        WriteChunk(writer, "LIST", 3, inner =>
        {
            inner.Write(Encoding.ASCII.GetBytes("abc"));
            inner.Write((byte)0);
        });

        WriteChunk(writer, "data", samples.Length * 2, inner => WriteSamples(inner, samples, 16));
        Finish(writer);

        return stream.ToArray();
    }

    private static byte[] Build(
        int dataBytes,
        int sampleRate,
        int channels,
        int bitsPerSample,
        ushort formatTag,
        short[]? samples = null,
        Action<BinaryWriter>? sampleWriter = null,
        bool extensible = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(0);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        WriteChunk(writer, "fmt ", extensible ? 40 : 16, inner =>
            WriteFormat(inner, sampleRate, channels, bitsPerSample, formatTag, extensible));

        WriteChunk(writer, "data", dataBytes, inner =>
        {
            if (sampleWriter is not null)
            {
                sampleWriter(inner);
            }
            else
            {
                WriteSamples(inner, samples ?? [], bitsPerSample);
            }
        });

        Finish(writer);

        return stream.ToArray();
    }

    private static void WriteFormat(
        BinaryWriter writer,
        int sampleRate,
        int channels,
        int bitsPerSample,
        ushort formatTag,
        bool extensible)
    {
        writer.Write(formatTag);
        writer.Write((ushort)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((ushort)(channels * (bitsPerSample / 8)));
        writer.Write((ushort)bitsPerSample);

        if (!extensible)
        {
            return;
        }

        writer.Write((ushort)22);
        writer.Write((ushort)bitsPerSample);
        writer.Write(0);

        // The sub-format GUID: a four-byte format tag with two bytes of padding, then the tail that
        // says it is a media-foundation audio subtype.
        writer.Write((ushort)(formatTag == 0xFFFE ? 1 : formatTag));
        writer.Write((ushort)0);
        writer.Write(new byte[] { 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71 });
    }

    private static void WriteSamples(BinaryWriter writer, short[] samples, int bitsPerSample)
    {
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }
    }

    private static void WriteChunk(BinaryWriter writer, string id, int size, Action<BinaryWriter> payload)
    {
        writer.Write(Encoding.ASCII.GetBytes(id));
        writer.Write(size);
        payload(writer);
    }

    /// <summary>Writes the RIFF size once every chunk is in, which is the only field that is not local.</summary>
    private static void Finish(BinaryWriter writer)
    {
        var length = (int)writer.BaseStream.Length;
        writer.BaseStream.Position = 4;
        writer.Write(length - 8);
    }

    /// <summary>The sample at <paramref name="index"/> of a decoded clip, as the 16-bit value it became.</summary>
    public static short SampleAt(AudioClip clip, int index) =>
        BinaryPrimitives.ReadInt16LittleEndian(clip.Samples.Span[(index * 2)..]);
}

using System.Buffers.Binary;

namespace Kolvir.Audio;

/// <summary>
/// Reads a RIFF WAVE file into a clip: the one decoder this version ships, written in managed code so
/// a beep costs nothing native.
/// </summary>
/// <remarks>
/// <para>
/// WAV first because it is what a UI sound is: short, uncompressed, and trivial to produce. It covers
/// the two things an application actually does - ship a click, and play a notification tone - without
/// dragging in Media Foundation on Windows or GStreamer on Linux for the privilege. Compressed
/// formats arrive later, as their own single-file decoders, and they decode into the same clip.
/// </para>
/// <para>
/// <b>Everything it accepts becomes 16-bit signed PCM.</b> 8-, 24- and 32-bit integers and 32-bit
/// floats are converted on the way in; the platform halves then have one format to open a stream
/// with. A file it cannot read throws rather than loading as silence, which is the difference between
/// a mistake an application can see and one it cannot.
/// </para>
/// <para>
/// The whole stream is read into memory: a clip is a sound effect, and the alternative - seeking a
/// stream a caller may not have given up - trades a real cost for a hypothetical one.
/// </para>
/// </remarks>
internal static class WavAudioClipReader
{
    private const ushort FormatPcm = 1;
    private const ushort FormatFloat = 3;
    private const ushort FormatExtensible = 0xFFFE;

    // The twelve bytes of every KSDATAFORMAT_SUBTYPE_* GUID after its four-byte format tag portion:
    // the tag says PCM or float, and this says it is one of the media-foundation audio subtypes.
    private static readonly byte[] SubFormatTail =
        [0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71];

    /// <summary>Decodes <paramref name="stream"/> and attaches the clip to <paramref name="engine"/>.</summary>
    /// <exception cref="InvalidDataException">The stream is not a WAVE file, or a required chunk is missing.</exception>
    /// <exception cref="NotSupportedException">The file holds a format this reader cannot decode.</exception>
    internal static AudioClip Read(AudioEngine engine, Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        var data = buffer.ToArray();

        if (data.Length < 12 ||
            !Matches(data, 0, "RIFF") ||
            !Matches(data, 8, "WAVE"))
        {
            throw new InvalidDataException("The stream is not a RIFF WAVE file.");
        }

        int channels = 0;
        int sampleRate = 0;
        int bitsPerSample = 0;
        ushort format = 0;
        bool haveFormat = false;

        int offset = 12;

        while (offset + 8 <= data.Length)
        {
            var chunkId = System.Text.Encoding.ASCII.GetString(data, offset, 4);
            var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 4, 4));

            if (chunkSize < 0 || offset + 8 + chunkSize > data.Length)
            {
                throw new InvalidDataException($"The '{chunkId}' chunk runs past the end of the stream.");
            }

            var payload = data.AsSpan(offset + 8, chunkSize);

            switch (chunkId)
            {
                case "fmt ":
                    ReadFormat(payload, out format, out channels, out sampleRate, out bitsPerSample);
                    haveFormat = true;
                    break;

                case "data":
                    if (!haveFormat)
                    {
                        throw new InvalidDataException("The stream has audio data before its format.");
                    }

                    return Build(engine, format, channels, sampleRate, bitsPerSample, payload);

                default:
                    // 'LIST', 'fact', 'cue ' and everything else: a WAV file may carry them and this
                    // reader has no use for any of them.
                    break;
            }

            // Chunks are word aligned, so an odd-sized one is followed by a padding byte.
            offset += 8 + chunkSize + (chunkSize & 1);
        }

        throw new InvalidDataException(haveFormat
            ? "The stream has no audio data."
            : "The stream has no format chunk.");
    }

    /// <summary>Reads a <c>fmt </c> chunk, including the extensible form that wraps the real tag.</summary>
    private static void ReadFormat(
        ReadOnlySpan<byte> payload,
        out ushort format,
        out int channels,
        out int sampleRate,
        out int bitsPerSample)
    {
        if (payload.Length < 16)
        {
            throw new InvalidDataException("The format chunk is too short.");
        }

        format = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        channels = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
        sampleRate = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(payload[14..]);

        if (format == FormatExtensible)
        {
            // WAVE_FORMAT_EXTENSIBLE: the real format is the first two bytes of the sub-format GUID at
            // offset 24 - a four-byte format tag with two bytes of padding - and the twelve bytes after
            // that identify the audio API rather than the encoding.
            if (payload.Length < 40)
            {
                throw new InvalidDataException("The extensible format chunk is too short.");
            }

            for (int i = 0; i < SubFormatTail.Length; i++)
            {
                if (payload[28 + i] != SubFormatTail[i])
                {
                    throw new NotSupportedException("The extensible format uses an unknown sub-format.");
                }
            }

            format = BinaryPrimitives.ReadUInt16LittleEndian(payload[24..]);
        }

        if (channels < 1 || channels > 16)
        {
            throw new NotSupportedException($"A WAV file with {channels} channels is not supported.");
        }

        if (sampleRate is < 1000 or > 384000)
        {
            throw new NotSupportedException($"A sample rate of {sampleRate} Hz is not supported.");
        }
    }

    /// <summary>Turns the samples into the 16-bit interleaved form the platform halves want.</summary>
    private static AudioClip Build(
        AudioEngine engine,
        ushort format,
        int channels,
        int sampleRate,
        int bitsPerSample,
        ReadOnlySpan<byte> payload)
    {
        if (format == FormatPcm && bitsPerSample is not (8 or 16 or 24 or 32))
        {
            throw new NotSupportedException($"A {bitsPerSample}-bit PCM WAV file is not supported.");
        }

        if (format == FormatFloat && bitsPerSample != 32)
        {
            throw new NotSupportedException($"A {bitsPerSample}-bit floating point WAV file is not supported.");
        }

        if (format is not (FormatPcm or FormatFloat))
        {
            throw new NotSupportedException(
                $"WAV format {format} is compressed, and this version decodes uncompressed audio only.");
        }

        int bytesPerSample = bitsPerSample / 8;
        int frameBytes = bytesPerSample * channels;
        int frames = payload.Length / frameBytes;
        var samples = new byte[frames * channels * 2];

        for (int frame = 0; frame < frames; frame++)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                var source = payload.Slice((frame * frameBytes) + (channel * bytesPerSample), bytesPerSample);
                short value = format == FormatFloat ? FromFloat(source) : FromInteger(source, bitsPerSample);
                int target = ((frame * channels) + channel) * 2;
                samples[target] = (byte)(value & 0xFF);
                samples[target + 1] = (byte)((value >> 8) & 0xFF);
            }
        }

        return new AudioClip(engine, samples, sampleRate, channels);
    }

    /// <summary>Converts an integer sample of 8, 16, 24 or 32 bits to 16 bits.</summary>
    private static short FromInteger(ReadOnlySpan<byte> source, int bitsPerSample) => bitsPerSample switch
    {
        // 8-bit WAV is unsigned, with 128 as silence, and every other width is signed.
        8 => (short)((source[0] - 128) << 8),
        16 => (short)(source[0] | (source[1] << 8)),
        24 => (short)(SignExtend24(source) >> 8),
        _ => (short)(BinaryPrimitives.ReadInt32LittleEndian(source) >> 16),
    };

    /// <summary>The 24-bit sample as a signed 32-bit value, sign bit and all.</summary>
    private static int SignExtend24(ReadOnlySpan<byte> source)
    {
        int value = source[0] | (source[1] << 8) | (source[2] << 16);

        return (value & 0x800000) == 0 ? value : value | unchecked((int)0xFF000000);
    }

    /// <summary>Converts a 32-bit float sample, which is nominally between -1 and 1, to 16 bits.</summary>
    private static short FromFloat(ReadOnlySpan<byte> source)
    {
        var value = BinaryPrimitives.ReadSingleLittleEndian(source);

        if (float.IsNaN(value))
        {
            return 0;
        }

        return (short)Math.Clamp(value * 32767f, short.MinValue, short.MaxValue);
    }

    private static bool Matches(byte[] data, int offset, string tag) =>
        data.AsSpan(offset, 4).SequenceEqual(System.Text.Encoding.ASCII.GetBytes(tag));
}

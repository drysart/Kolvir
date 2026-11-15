namespace Kolvir.WoffFonts;

/// <summary>
/// Rebuilds the <c>glyf</c> and <c>loca</c> tables from the re-encoded form WOFF 2 stores them in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is the expensive half of the feature.</b> WOFF 1.0 only compresses; WOFF 2 takes the
/// glyph outlines apart. A glyph's contours, its point counts, its per-point flags, its coordinate
/// deltas and its instructions each go into their own stream - groups of like data that compress far
/// better together than interleaved - and the coordinates are re-encoded as "triplets": one flag byte
/// per point, and a variable-length signed delta pair whose width and base value are chosen from a
/// table of 128 cases. Rebuilding the font means undoing all of that, glyph by glyph, and writing the
/// standard TrueType glyph records back out. <c>loca</c> is not stored at all, because the offsets
/// are whatever the reconstruction produced.
/// </para>
/// <para>
/// The encoding is defined by the WOFF 2 specification, section 5.1 (the streams) and section 5.2
/// (the triplets). Only the streams described there are read; anything this decoder does not need -
/// the extended metadata, private data, the bounding-box hints for glyphs whose box is derived - is
/// skipped or recomputed.
/// </para>
/// <para>
/// <b>Bounding boxes are recomputed when the file omits them</b>, which is normal for simple glyphs
/// whose box already matched the outline. A composite glyph must carry one, because computing it needs
/// the components resolved - the specification says to reject the file otherwise, and it is right to.
/// </para>
/// <para>
/// The reconstructed table will not be byte-identical to the original: the flag encoding has several
/// valid spellings and this writer picks the simplest. It is the same outlines, which is what the
/// format promises and what the tests check by comparing rendered glyphs rather than bytes.
/// </para>
/// </remarks>
internal static class Woff2Glyf
{
    private const int HeaderSize = 36;

    /// <summary>Flags in a composite glyph's component record, as the TrueType format numbers them.</summary>
    private const int Arg1And2AreWords = 0x0001;
    private const int MoreComponents = 0x0020;
    private const int WeHaveAScale = 0x0008;
    private const int WeHaveAnXAndYScale = 0x0040;
    private const int WeHaveATwoByTwo = 0x0080;
    private const int WeHaveInstructions = 0x0100;

    /// <summary>Per-point flags in a reconstructed glyph, as the TrueType format numbers them.</summary>
    private const byte OnCurvePoint = 0x01;
    private const byte XShortVector = 0x02;
    private const byte YShortVector = 0x04;
    private const byte XIsSameOrPositive = 0x10;
    private const byte YIsSameOrPositive = 0x20;
    private const byte OverlapSimple = 0x40;

    /// <summary>Rebuilds both tables.</summary>
    /// <param name="transformed">The transformed <c>glyf</c> stream, which is the whole glyph data block.</param>
    /// <param name="numGlyphs">The glyph count the font declares, from <c>maxp</c>.</param>
    /// <param name="indexFormat">The <c>loca</c> offset width from <c>head</c>: 0 for 16-bit offsets.</param>
    /// <returns>
    /// The two tables' bytes, and each glyph's xMin - which the <c>hmtx</c> transform may need to
    /// rebuild left side bearings.
    /// </returns>
    /// <exception cref="InvalidDataException">The stream is damaged or contradicts the font's own tables.</exception>
    public static (byte[] Glyf, byte[] Loca, short[] XMins) Reconstruct(
        ReadOnlySpan<byte> transformed,
        int numGlyphs,
        int indexFormat)
    {
        if (transformed.Length < HeaderSize)
        {
            throw new InvalidDataException("The transformed glyf table is truncated.");
        }

        var optionFlags = SfntBuilder.ReadUInt16(transformed, 2);
        var streamGlyphs = SfntBuilder.ReadUInt16(transformed, 4);
        var streamIndexFormat = SfntBuilder.ReadInt16(transformed, 6);

        if (streamGlyphs != numGlyphs)
        {
            throw new InvalidDataException(
                $"The transformed glyf table declares {streamGlyphs} glyphs; maxp declares {numGlyphs}.");
        }

        if (streamIndexFormat != indexFormat)
        {
            throw new InvalidDataException(
                $"The transformed glyf table declares loca format {streamIndexFormat}; head declares {indexFormat}.");
        }

        var sizes = new int[7];
        for (var i = 0; i < sizes.Length; i++)
        {
            var size = SfntBuilder.ReadUInt32(transformed, 8 + (i * 4));
            if (size > (uint)transformed.Length)
            {
                throw new InvalidDataException($"A transformed glyf substream declares {size} bytes, which is more than the table holds.");
            }

            sizes[i] = (int)size;
        }

        var at = HeaderSize;
        var nContours = Substream(transformed, ref at, sizes[0], "nContour");
        var nPoints = Substream(transformed, ref at, sizes[1], "nPoints");
        var flags = Substream(transformed, ref at, sizes[2], "flag");
        var glyphs = Substream(transformed, ref at, sizes[3], "glyph");
        var composites = Substream(transformed, ref at, sizes[4], "composite");
        var boxes = Substream(transformed, ref at, sizes[5], "bbox");
        var instructions = Substream(transformed, ref at, sizes[6], "instruction");

        // The optional overlap bitmap is the one part of the table the header does not size, so it is
        // found by position: whatever is left after the seven sized streams.
        var overlapBitmap = (optionFlags & 1) != 0
            ? Substream(transformed, ref at, (numGlyphs + 7) / 8, "overlap")
            : default;

        var boxBitmapLength = 4 * ((numGlyphs + 31) / 32);
        if (boxes.Length < boxBitmapLength)
        {
            throw new InvalidDataException("The transformed glyf bounding box stream is too short for its bitmap.");
        }

        var boxBitmap = boxes[..boxBitmapLength];
        var boxValues = boxes[boxBitmapLength..];

        var builder = new GlyfBuilder();
        var offsets = new int[numGlyphs + 1];
        var xMins = new short[numGlyphs];

        var nContourCursor = 0;
        var nPointsCursor = 0;
        var flagCursor = 0;
        var glyphCursor = 0;
        var compositeCursor = 0;
        var boxCursor = 0;
        var instructionCursor = 0;

        for (var glyph = 0; glyph < numGlyphs; glyph++)
        {
            // Every glyph starts on a four byte boundary, which is what lets a 16-bit loca address it
            // (its offsets are halves) and what a reader may assume about word alignment.
            builder.PadToWord();
            offsets[glyph] = builder.Length;

            if (nContourCursor + 2 > nContours.Length)
            {
                throw new InvalidDataException("The transformed glyf contour stream is truncated.");
            }

            var contours = SfntBuilder.ReadInt16(nContours, nContourCursor);
            nContourCursor += 2;

            var hasBox = IsBoxSet(boxBitmap, glyph);

            if (contours == 0)
            {
                // An empty glyph has no record at all, so its box must not have been stored.
                if (hasBox)
                {
                    throw new InvalidDataException($"Glyph {glyph} is empty but carries a bounding box.");
                }

                continue;
            }

            if (contours == -1)
            {
                if (!hasBox)
                {
                    throw new InvalidDataException($"Composite glyph {glyph} has no bounding box.");
                }

                xMins[glyph] = ReconstructComposite(
                    builder,
                    glyph,
                    composites,
                    ref compositeCursor,
                    glyphs,
                    ref glyphCursor,
                    instructions,
                    ref instructionCursor,
                    boxValues,
                    ref boxCursor);
            }
            else
            {
                xMins[glyph] = ReconstructSimple(
                    builder,
                    glyph,
                    contours,
                    nPoints,
                    ref nPointsCursor,
                    flags,
                    ref flagCursor,
                    glyphs,
                    ref glyphCursor,
                    instructions,
                    ref instructionCursor,
                    hasBox,
                    boxValues,
                    ref boxCursor,
                    IsOverlapSet(overlapBitmap, glyph));
            }
        }

        offsets[numGlyphs] = builder.PadToWord();

        var glyf = builder.ToArray();
        var loca = BuildLoca(offsets, indexFormat);
        return (glyf, loca, xMins);
    }

    private static short ReconstructSimple(
        GlyfBuilder builder,
        int glyph,
        int contourCount,
        ReadOnlySpan<byte> nPoints,
        ref int nPointsCursor,
        ReadOnlySpan<byte> flags,
        ref int flagCursor,
        ReadOnlySpan<byte> glyphStream,
        ref int glyphCursor,
        ReadOnlySpan<byte> instructions,
        ref int instructionCursor,
        bool hasBox,
        ReadOnlySpan<byte> boxValues,
        ref int boxCursor,
        bool overlap)
    {
        var endPoints = new ushort[contourCount];
        var total = 0;

        for (var contour = 0; contour < contourCount; contour++)
        {
            var points = Woff2Format.Read255UInt16(nPoints, ref nPointsCursor);
            total += points;
            endPoints[contour] = (ushort)(total - 1);
        }

        if (flagCursor + total > flags.Length)
        {
            throw new InvalidDataException($"Glyph {glyph} needs {total} flags but the flag stream has {flags.Length - flagCursor}.");
        }

        var pointFlags = flags.Slice(flagCursor, total);
        flagCursor += total;

        var pointsRead = new Point[total];
        var x = 0;
        var y = 0;

        for (var i = 0; i < total; i++)
        {
            var flag = pointFlags[i];
            var onCurve = (flag & 0x80) == 0;

            DecodeTriplet(flag & 0x7F, glyphStream, ref glyphCursor, glyph, out var dx, out var dy);
            x += dx;
            y += dy;
            pointsRead[i] = new Point(x, y, onCurve);
        }

        var instructionLength = Woff2Format.Read255UInt16(glyphStream, ref glyphCursor);
        if (instructionCursor + instructionLength > instructions.Length)
        {
            throw new InvalidDataException($"Glyph {glyph} needs {instructionLength} instruction bytes that are not there.");
        }

        var glyphInstructions = instructions.Slice(instructionCursor, instructionLength);
        instructionCursor += instructionLength;

        short xMin, yMin, xMax, yMax;
        if (hasBox)
        {
            (xMin, yMin, xMax, yMax) = ReadBox(boxValues, ref boxCursor, glyph);
        }
        else
        {
            (xMin, yMin, xMax, yMax) = ComputeBox(pointsRead);
        }

        builder.WriteInt16((short)contourCount);
        builder.WriteBox(xMin, yMin, xMax, yMax);

        foreach (var endPoint in endPoints)
        {
            builder.WriteUInt16(endPoint);
        }

        builder.WriteUInt16((ushort)instructionLength);
        builder.Write(glyphInstructions);

        WritePoints(builder, pointsRead, overlap);
        return xMin;
    }

    private static short ReconstructComposite(
        GlyfBuilder builder,
        int glyph,
        ReadOnlySpan<byte> composites,
        ref int compositeCursor,
        ReadOnlySpan<byte> glyphStream,
        ref int glyphCursor,
        ReadOnlySpan<byte> instructions,
        ref int instructionCursor,
        ReadOnlySpan<byte> boxValues,
        ref int boxCursor)
    {
        var (xMin, yMin, xMax, yMax) = ReadBox(boxValues, ref boxCursor, glyph);
        builder.WriteInt16(-1);
        builder.WriteBox(xMin, yMin, xMax, yMax);

        var haveInstructions = false;
        var more = true;

        while (more)
        {
            if (compositeCursor + 2 > composites.Length)
            {
                throw new InvalidDataException($"Composite glyph {glyph} runs past the composite stream.");
            }

            var componentFlags = SfntBuilder.ReadUInt16(composites, compositeCursor);
            compositeCursor += 2;

            // glyphIndex, the argument pair, and any component transform: copied through, because the
            // transform is stored rather than recomputed.
            var rest = 2
                + ((componentFlags & Arg1And2AreWords) != 0 ? 4 : 2)
                + ((componentFlags & WeHaveAScale) != 0 ? 2 : 0)
                + ((componentFlags & WeHaveAnXAndYScale) != 0 ? 4 : 0)
                + ((componentFlags & WeHaveATwoByTwo) != 0 ? 8 : 0);

            if (compositeCursor + rest > composites.Length)
            {
                throw new InvalidDataException($"Composite glyph {glyph} runs past the composite stream.");
            }

            builder.WriteUInt16(componentFlags);
            builder.Write(composites.Slice(compositeCursor, rest));
            compositeCursor += rest;

            haveInstructions |= (componentFlags & WeHaveInstructions) != 0;
            more = (componentFlags & MoreComponents) != 0;
        }

        if (haveInstructions)
        {
            var instructionLength = Woff2Format.Read255UInt16(glyphStream, ref glyphCursor);
            if (instructionCursor + instructionLength > instructions.Length)
            {
                throw new InvalidDataException($"Composite glyph {glyph} needs {instructionLength} instruction bytes that are not there.");
            }

            builder.WriteUInt16((ushort)instructionLength);
            builder.Write(instructions.Slice(instructionCursor, instructionLength));
            instructionCursor += instructionLength;
        }

        return xMin;
    }

    /// <summary>
    /// Writes the standard per-point flags and coordinates for a glyph.
    /// </summary>
    /// <remarks>
    /// The TrueType encoding lets a point say "same as the last one" (no bytes), "one byte, sign in the
    /// flag" or "two bytes". This writer picks the shortest of those per coordinate and never uses the
    /// repeat flag, which is a valid spelling of the same outlines - the specification allows any, and
    /// the format's promise is the glyph, not the bytes.
    /// </remarks>
    private static void WritePoints(GlyfBuilder builder, Point[] points, bool overlap)
    {
        var flags = new byte[points.Length];
        var previousX = 0;
        var previousY = 0;

        for (var i = 0; i < points.Length; i++)
        {
            var point = points[i];
            var dx = point.X - previousX;
            var dy = point.Y - previousY;

            byte flag = point.OnCurve ? OnCurvePoint : (byte)0;
            if (point.OnCurve && i == 0 && overlap)
            {
                flag |= OverlapSimple;
            }

            if (dx == 0)
            {
                flag |= XIsSameOrPositive;
            }
            else if (Math.Abs(dx) <= 255)
            {
                flag |= XShortVector;
                if (dx > 0)
                {
                    flag |= XIsSameOrPositive;
                }
            }

            if (dy == 0)
            {
                flag |= YIsSameOrPositive;
            }
            else if (Math.Abs(dy) <= 255)
            {
                flag |= YShortVector;
                if (dy > 0)
                {
                    flag |= YIsSameOrPositive;
                }
            }

            flags[i] = flag;
            previousX = point.X;
            previousY = point.Y;
        }

        builder.Write(flags);

        previousX = 0;
        for (var i = 0; i < points.Length; i++)
        {
            var dx = points[i].X - previousX;
            previousX = points[i].X;
            WriteCoordinate(builder, dx, (flags[i] & XShortVector) != 0, (flags[i] & XIsSameOrPositive) != 0);
        }

        previousY = 0;
        for (var i = 0; i < points.Length; i++)
        {
            var dy = points[i].Y - previousY;
            previousY = points[i].Y;
            WriteCoordinate(builder, dy, (flags[i] & YShortVector) != 0, (flags[i] & YIsSameOrPositive) != 0);
        }
    }

    private static void WriteCoordinate(GlyfBuilder builder, int delta, bool isShort, bool isSameOrPositive)
    {
        if (isShort)
        {
            builder.WriteByte((byte)Math.Abs(delta));
        }
        else if (!isSameOrPositive)
        {
            builder.WriteInt16((short)delta);
        }
    }

    /// <summary>
    /// Decodes one point's coordinate pair from the triplet table.
    /// </summary>
    /// <remarks>
    /// The 128 cases are four families of increasing width - one coordinate byte holding 0/8, 8/0 or
    /// 4/4 bits, then two bytes of 8/8, then three of 12/12, then four of 16/16 - and within each
    /// family a base value and a pair of signs picked by position. The families and their bases are
    /// section 5.2's table; the bit order inside a coordinate byte group is most-significant-first,
    /// x before y.
    /// </remarks>
    private static void DecodeTriplet(int index, ReadOnlySpan<byte> data, ref int offset, int glyph, out int dx, out int dy)
    {
        int bytes;
        int xBits;
        int yBits;
        int xBase;
        int yBase;
        bool xNegative;
        bool yNegative;

        if (index < 10)
        {
            bytes = 1; xBits = 0; yBits = 8;
            xBase = 0; yBase = (index / 2) * 256;
            xNegative = false; yNegative = (index & 1) == 0;
        }
        else if (index < 20)
        {
            var n = index - 10;
            bytes = 1; xBits = 8; yBits = 0;
            xBase = (n / 2) * 256; yBase = 0;
            xNegative = (n & 1) == 0; yNegative = false;
        }
        else if (index < 84)
        {
            var n = index - 20;
            bytes = 1; xBits = 4; yBits = 4;
            xBase = 1 + (16 * (n / 16)); yBase = 1 + (16 * ((n / 4) % 4));
            xNegative = (n & 1) == 0; yNegative = (n & 2) == 0;
        }
        else if (index < 120)
        {
            var n = index - 84;
            bytes = 2; xBits = 8; yBits = 8;
            xBase = 1 + (256 * (n / 12)); yBase = 1 + (256 * ((n / 4) % 3));
            xNegative = (n & 1) == 0; yNegative = (n & 2) == 0;
        }
        else if (index < 124)
        {
            var n = index - 120;
            bytes = 3; xBits = 12; yBits = 12;
            xBase = 0; yBase = 0;
            xNegative = (n & 1) == 0; yNegative = (n & 2) == 0;
        }
        else
        {
            var n = index - 124;
            bytes = 4; xBits = 16; yBits = 16;
            xBase = 0; yBase = 0;
            xNegative = (n & 1) == 0; yNegative = (n & 2) == 0;
        }

        if (offset + bytes > data.Length)
        {
            throw new InvalidDataException($"Glyph {glyph} runs past the glyph stream.");
        }

        var packed = 0;
        for (var i = 0; i < bytes; i++)
        {
            packed = (packed << 8) | data[offset + i];
        }

        offset += bytes;

        var xValue = xBits == 0 ? 0 : (packed >> yBits) & ((1 << xBits) - 1);
        var yValue = yBits == 0 ? 0 : packed & ((1 << yBits) - 1);

        dx = xBits == 0 ? 0 : xNegative ? -(xBase + xValue) : xBase + xValue;
        dy = yBits == 0 ? 0 : yNegative ? -(yBase + yValue) : yBase + yValue;
    }

    private static (short XMin, short YMin, short XMax, short YMax) ReadBox(
        ReadOnlySpan<byte> values, ref int offset, int glyph)
    {
        if (offset + 8 > values.Length)
        {
            throw new InvalidDataException($"Glyph {glyph} needs a bounding box that is not in the stream.");
        }

        var xMin = (short)((values[offset] << 8) | values[offset + 1]);
        var yMin = (short)((values[offset + 2] << 8) | values[offset + 3]);
        var xMax = (short)((values[offset + 4] << 8) | values[offset + 5]);
        var yMax = (short)((values[offset + 6] << 8) | values[offset + 7]);
        offset += 8;
        return (xMin, yMin, xMax, yMax);
    }

    private static (short XMin, short YMin, short XMax, short YMax) ComputeBox(Point[] points)
    {
        if (points.Length == 0)
        {
            return (0, 0, 0, 0);
        }

        var xMin = short.MaxValue;
        var yMin = short.MaxValue;
        var xMax = short.MinValue;
        var yMax = short.MinValue;

        foreach (var point in points)
        {
            xMin = (short)Math.Min(xMin, point.X);
            yMin = (short)Math.Min(yMin, point.Y);
            xMax = (short)Math.Max(xMax, point.X);
            yMax = (short)Math.Max(yMax, point.Y);
        }

        return (xMin, yMin, xMax, yMax);
    }

    private static byte[] BuildLoca(int[] offsets, int indexFormat)
    {
        var loca = new byte[offsets.Length * (indexFormat == 0 ? 2 : 4)];

        for (var i = 0; i < offsets.Length; i++)
        {
            if (indexFormat == 0)
            {
                if ((offsets[i] & 1) != 0 || offsets[i] / 2 > ushort.MaxValue)
                {
                    throw new InvalidDataException("The reconstructed glyf table does not fit 16-bit loca offsets.");
                }

                SfntBuilder.WriteUInt16(loca, i * 2, (ushort)(offsets[i] / 2));
            }
            else
            {
                SfntBuilder.WriteUInt32(loca, i * 4, (uint)offsets[i]);
            }
        }

        return loca;
    }

    private static bool IsBoxSet(ReadOnlySpan<byte> bitmap, int glyph) =>
        (bitmap[glyph >> 3] & (0x80 >> (glyph & 7))) != 0;

    private static bool IsOverlapSet(ReadOnlySpan<byte> bitmap, int glyph) =>
        !bitmap.IsEmpty && (bitmap[glyph >> 3] & (0x80 >> (glyph & 7))) != 0;

    private static ReadOnlySpan<byte> Substream(ReadOnlySpan<byte> data, ref int at, int length, string name)
    {
        if (length < 0 || at + length > data.Length)
        {
            throw new InvalidDataException($"The transformed glyf {name} stream is missing or truncated.");
        }

        var slice = data.Slice(at, length);
        at += length;
        return slice;
    }

    private readonly record struct Point(int X, int Y, bool OnCurve);

    /// <summary>A growable writer for the reconstructed table, which pads each glyph to a word boundary.</summary>
    private sealed class GlyfBuilder
    {
        private byte[] _bytes = new byte[64 * 1024];
        private int _length;

        public int Length => _length;

        public void WriteByte(byte value)
        {
            Ensure(1);
            _bytes[_length++] = value;
        }

        public void WriteUInt16(ushort value)
        {
            Ensure(2);
            SfntBuilder.WriteUInt16(_bytes, _length, value);
            _length += 2;
        }

        public void WriteInt16(short value) => WriteUInt16((ushort)value);

        public void WriteBox(short xMin, short yMin, short xMax, short yMax)
        {
            WriteInt16(xMin);
            WriteInt16(yMin);
            WriteInt16(xMax);
            WriteInt16(yMax);
        }

        public void Write(ReadOnlySpan<byte> data)
        {
            Ensure(data.Length);
            data.CopyTo(_bytes.AsSpan(_length));
            _length += data.Length;
        }

        /// <summary>Pads to a four byte boundary, so a 16-bit loca can point at every glyph.</summary>
        public int PadToWord()
        {
            var padded = SfntBuilder.Align4(_length);
            Ensure(padded - _length);
            while (_length < padded)
            {
                _bytes[_length++] = 0;
            }

            return _length;
        }

        public byte[] ToArray() => _bytes.AsSpan(0, _length).ToArray();

        private void Ensure(int extra)
        {
            if (_length + extra <= _bytes.Length)
            {
                return;
            }

            var grown = new byte[Math.Max(_bytes.Length * 2, _length + extra)];
            _bytes.AsSpan(0, _length).CopyTo(grown);
            _bytes = grown;
        }
    }
}

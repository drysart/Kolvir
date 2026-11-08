using System.Text;
using SkiaSharp;
using Svg;
using Svg.Model;
using Svg.Skia;

namespace XPlatUI.Svg;

/// <summary>
/// The parsing half of the feature: bytes in, a picture out, with the refusals the security posture
/// depends on.
/// </summary>
/// <remarks>
/// <para>
/// Parsing is deliberately uninterested in where the bytes came from. The provider has a stream and
/// an extension hint and nothing else, so everything here works from the bytes alone, which is what
/// makes a stream and a file behave the same way.
/// </para>
/// <para>
/// The refusals are not advisory. A document is rejected before it is parsed when it declares a
/// DTD or an entity, because entity expansion is the one part of XML parsing whose cost is
/// exponential in the input and cannot be bounded by looking at the document's size. Everything
/// else is refused by Svg.Skia's own secure processing mode and external-resource policy: external
/// files, network references and <c>xlink:href</c> to anything outside the document are not
/// resolved, and script is not run. A <c>data:</c> URI is the one reference that is honoured, because
/// its bytes are part of the document rather than somewhere else on the machine.
/// </para>
/// </remarks>
internal static class XPlatSvgDocument
{
    /// <summary>The largest document that will be parsed, in bytes.</summary>
    /// <remarks>
    /// An icon is a few kilobytes and a detailed illustration a few hundred, so a megabyte is
    /// generous. The cap exists because the whole document is in memory by the time it is parsed,
    /// and a toolkit should have a limit it chose rather than one it inherited from an allocation.
    /// </remarks>
    internal const int MaximumDocumentBytes = 1024 * 1024;

    private static readonly byte[] DoctypeLower = "<!doctype"u8.ToArray();
    private static readonly byte[] DoctypeUpper = "<!DOCTYPE"u8.ToArray();
    private static readonly byte[] EntityLower = "<!entity"u8.ToArray();
    private static readonly byte[] EntityUpper = "<!ENTITY"u8.ToArray();
    private static readonly byte[] DoctypeLowerUtf16 = Encoding.Unicode.GetBytes("<!doctype");
    private static readonly byte[] DoctypeUpperUtf16 = Encoding.Unicode.GetBytes("<!DOCTYPE");
    private static readonly byte[] EntityLowerUtf16 = Encoding.Unicode.GetBytes("<!entity");
    private static readonly byte[] EntityUpperUtf16 = Encoding.Unicode.GetBytes("<!ENTITY");

    /// <summary>
    /// Answers whether a stream's first bytes are plausibly an SVG document.
    /// </summary>
    /// <remarks>
    /// The root element can be preceded by a byte-order mark, whitespace, an XML declaration, a
    /// comment, a DOCTYPE and processing instructions, so the test is "the first element-looking
    /// thing in the first block is <c>svg</c>" rather than "the file starts with a magic number".
    /// A prefix this short is enough: a provider's <see cref="Decode"/> is where a longer look
    /// happens, and it reports a document that turns out not to be SVG as a decode failure.
    /// </remarks>
    internal static bool LooksLikeSvg(ReadOnlySpan<byte> header)
    {
        int index = 0;

        // A byte-order mark, in either UTF flavour.
        if (header.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            index = 3;
        }
        else if (header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || header.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            // UTF-16 cannot be searched as ASII, and Svg.Skia reads UTF-16 through the XML reader
            // when the declaration or the mark says so. Recognising the mark is enough here.
            return ContainsUtf16(header, "svg");
        }

        var text = header[index..];

        // Skip whitespace, the XML declaration, comments and other directives, then require the
        // root element to be svg, optionally with an SVG namespace prefix.
        while (true)
        {
            text = TrimStart(text);
            if (text.IsEmpty)
            {
                return false;
            }

            if (text.StartsWith("<?xml"u8))
            {
                text = After(text, "?>"u8);
            }
            else if (text.StartsWith("<!--"u8))
            {
                text = After(text, "-->"u8);
            }
            else if (text.StartsWith("<!doctype"u8) || text.StartsWith("<!DOCTYPE"u8))
            {
                text = After(text, ">"u8);
            }
            else if (text.StartsWith("]>"u8))
            {
                // The end of a DTD's internal subset, which is the one thing a directive can leave
                // behind: `<!DOCTYPE svg [ ... ]>` ends at the `]>`, not at the first `>` inside it.
                text = text[2..];
            }
            else if (text.StartsWith("<!"u8))
            {
                text = After(text, ">"u8);
            }
            else if (text.StartsWith("<svg"u8) || text.StartsWith("<SVG"u8))
            {
                return true;
            }
            else
            {
                // A namespaced root such as <svg:svg>, or a capitalisation the fast path missed.
                // Anything whose root element is not svg is not claimed: an HTML file or an XML
                // file of another kind is a format this provider does not handle, and claiming it
                // would turn a load that should quietly fail into a parse error.
                return RootElementIsSvg(text);
            }
        }
    }

    /// <summary>Reads the root element's name and answers whether its local name is <c>svg</c>.</summary>
    private static bool RootElementIsSvg(ReadOnlySpan<byte> text)
    {
        if (text.IsEmpty || text[0] != (byte)'<')
        {
            return false;
        }

        int index = 1;
        while (index < text.Length && (IsNameByte(text[index]) || text[index] == (byte)':'))
        {
            index++;
        }

        if (index <= 1)
        {
            return false;
        }

        var name = text[1..index];

        // A prefixed name such as svg:svg: the local part after the last colon is what matters.
        int colon = name.LastIndexOf((byte)':');
        if (colon >= 0)
        {
            name = name[(colon + 1)..];
        }

        return System.Text.Ascii.EqualsIgnoreCase(name, "svg"u8);
    }

    private static bool IsNameByte(byte value) =>
        char.IsAsciiLetterOrDigit((char)value) || value is (byte)'-' or (byte)'_' or (byte)'.';

    /// <summary>
    /// Parses a document and returns a recording of it, which the caller owns.
    /// </summary>
    /// <param name="svg">The parsed document, owned by the caller and reused across recordings.</param>
    /// <returns>A picture whose bounds are the document's own.</returns>
    internal static SKPicture Record(SKSvg svg)
    {
        var source = svg.Picture
                     ?? throw new InvalidOperationException("The SVG document produced no picture.");

        using var recorder = new SKPictureRecorder();
        var bounds = source.CullRect;

        var canvas = recorder.BeginRecording(bounds);
        canvas.DrawPicture(source);
        return recorder.EndRecording();
    }

    /// <summary>Parses a document with the feature's refusals in force.</summary>
    /// <param name="content">The document's bytes.</param>
    /// <param name="currentColor">
    /// What <c>currentColor</c> resolves to, or null for SVG's own default of black.
    /// </param>
    /// <exception cref="InvalidOperationException">The document declares a DTD or an entity.</exception>
    internal static SKSvg Parse(ReadOnlySpan<byte> content, SKColor? currentColor)
    {
        if (content.Length > MaximumDocumentBytes)
        {
            throw new InvalidOperationException(
                $"The SVG document is larger than the {MaximumDocumentBytes} bytes this feature parses.");
        }

        if (DeclaresDtdOrEntity(content))
        {
            throw new InvalidOperationException(
                "The SVG document declares a DTD or an entity, which this feature refuses: entity expansion is unbounded work on a document of any size.");
        }

        // The document's own defaults are already the ones this feature wants: script is off
        // (EnableJavaScript is false, and there is no runtime to run it with), and a document that
        // is parsed with the secure processing mode below ignores a <script> element regardless.
        // Settings are deliberately not mutated here: SKSvg's settings object is shared under the
        // write, so setting one would change behaviour for every other Svg.Skia user in the process.
        var parameters = new SvgParameters(null, null)
        {
            CurrentColor = currentColor is { } color
                ? System.Drawing.Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue)
                : null,
            LoadOptions = new SvgDocumentLoadOptions
            {
                // The setting that matters most here, and the reason it is not the looser default:
                // SameDocumentAndDataOnly resolves a reference that points inside the document -
                // `<use href="#shape">` - and a data: URI, whose bytes are already in the
                // document, and refuses everything else. Measured: a data: image renders under this
                // policy and shows only a broken-image placeholder under Disabled, while a file://
                // image and an http image render as nothing under either. The policy is therefore
                // what decides the one reference that is honoured, and file and network references
                // have two independent reasons not to resolve rather than one.
                ExternalResources = SvgExternalResourcePolicy.SameDocumentAndDataOnly,
                ProcessingMode = SvgProcessingMode.SecureStatic,
            },
        };

        var svg = new SKSvg();
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            if (svg.Load(stream, parameters) is null)
            {
                throw new InvalidOperationException("The SVG document could not be parsed.");
            }

            return svg;
        }
        catch
        {
            svg.Dispose();
            throw;
        }
    }

    /// <summary>Finds a DTD or entity declaration, in either encoding that can carry one.</summary>
    internal static bool DeclaresDtdOrEntity(ReadOnlySpan<byte> content) =>
        content.IndexOf(DoctypeLower) >= 0
        || content.IndexOf(DoctypeUpper) >= 0
        || content.IndexOf(EntityLower) >= 0
        || content.IndexOf(EntityUpper) >= 0
        || content.IndexOf(DoctypeLowerUtf16) >= 0
        || content.IndexOf(DoctypeUpperUtf16) >= 0
        || content.IndexOf(EntityLowerUtf16) >= 0
        || content.IndexOf(EntityUpperUtf16) >= 0;

    private static bool ContainsUtf16(ReadOnlySpan<byte> header, string value)
    {
        // The UTF-16 comparison is done on bytes rather than on a decoded string: a truncated
        // multi-byte character at the end of the header must not throw, and a search for the
        // element name does not need the document to decode cleanly.
        return header.IndexOf(Encoding.Unicode.GetBytes(value)) >= 0
               || header.IndexOf(Encoding.BigEndianUnicode.GetBytes(value)) >= 0;
    }

    private static ReadOnlySpan<byte> TrimStart(ReadOnlySpan<byte> text)
    {
        int index = 0;
        while (index < text.Length && (text[index] == (byte)' ' || text[index] == (byte)'\t'
                                       || text[index] == (byte)'\r' || text[index] == (byte)'\n'))
        {
            index++;
        }

        return text[index..];
    }

    private static ReadOnlySpan<byte> After(ReadOnlySpan<byte> text, ReadOnlySpan<byte> terminator)
    {
        int end = text.IndexOf(terminator);
        return end < 0 ? [] : text[(end + terminator.Length)..];
    }
}

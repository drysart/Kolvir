using SkiaSharp;

namespace XPlatUI.Svg;

/// <summary>
/// Loads SVG into an <see cref="XPlatImage"/>, including the recolour that a theme needs.
/// </summary>
/// <remarks>
/// <para>
/// For the plain case there is nothing here that <see cref="XPlatImage.Load(string)"/> does not
/// already do - register the provider with <see cref="XPlatSvg.AddSvgImages"/> and load - and that
/// is the route to prefer, because it is the one every format shares and it is the one that reports
/// a failure through <see cref="XPlatImage.AddLoadFailedHandler"/>.
/// </para>
/// <para>
/// This class exists for the one thing <c>Load</c> cannot express: an icon that draws with
/// <c>currentColor</c> and should follow the palette. The colour is applied while the document is
/// parsed, because that is when <c>currentColor</c> is resolved, so the parse cache is keyed on the
/// colour as well as the bytes: a second colour is a second parse, and every load after that at the
/// same two colours is a cache hit and a fresh recording, not a parse.
/// </para>
/// <para>
/// Using it does not require <see cref="XPlatSvg.AddSvgImages"/>, and calling it does not register
/// anything: it goes straight to the renderer. An application that wants SVG from
/// <c>XPlatImage.Load</c> <em>and</em> a recolour helper calls both.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// image.LoadSvg("icons/star.svg", XPlatTheme.Foreground);
/// </code>
/// </example>
public static class XPlatImageSvgExtensions
{
    /// <summary>
    /// Loads an SVG document from disk into a control, optionally resolving <c>currentColor</c> to
    /// <paramref name="currentColor"/>.
    /// </summary>
    /// <param name="image">The control to load into.</param>
    /// <param name="path">The file to read.</param>
    /// <param name="currentColor">
    /// What <c>currentColor</c> resolves to, or null for SVG's own default of black. Elements that
    /// name a colour of their own keep it; only <c>currentColor</c> follows this.
    /// </param>
    /// <returns>True when the document loaded; false when it could not be read or parsed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="path"/> is null.</exception>
    /// <remarks>
    /// A failure returns false rather than raising, because this is not <c>Load</c> and there is no
    /// provider claim to report - call <see cref="XPlatImage.Load(string)"/> with the provider
    /// registered when the failure has to reach an <c>AddLoadFailedHandler</c> subscription.
    /// The control takes ownership of what is loaded, so <see cref="XPlatImage.DisposePictureWithControl"/>
    /// is left set.
    /// </remarks>
    public static bool LoadSvg(this XPlatImage image, string path, SKColor? currentColor = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(path);

        byte[] content;
        try
        {
            content = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }

        return LoadSvg(image, content, currentColor);
    }

    /// <summary>
    /// Loads an SVG document from a stream into a control, optionally resolving <c>currentColor</c>.
    /// </summary>
    /// <param name="image">The control to load into.</param>
    /// <param name="stream">The stream to read; it is read to the end and not retained.</param>
    /// <param name="currentColor">What <c>currentColor</c> resolves to, or null for black.</param>
    /// <returns>True when the document loaded; false when it could not be read or parsed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="stream"/> is null.</exception>
    public static bool LoadSvg(this XPlatImage image, Stream stream, SKColor? currentColor = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return LoadSvg(image, buffer.GetBuffer().AsSpan(0, (int)buffer.Length), currentColor);
    }

    private static bool LoadSvg(XPlatImage image, ReadOnlySpan<byte> content, SKColor? currentColor)
    {
        SKPicture picture;
        try
        {
            picture = XPlatSvgCache.Get(content, currentColor).Record();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }

        image.Picture = picture;

        // Ownership last, because setting Picture first replaces whatever was there under the
        // ownership rule that was in force a moment ago.
        image.DisposePictureWithControl = true;
        return true;
    }
}

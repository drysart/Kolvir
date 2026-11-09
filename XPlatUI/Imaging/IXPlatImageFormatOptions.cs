namespace XPlatUI;

/// <summary>
/// What a caller can tell the format provider that decodes its bytes, without core knowing what any
/// of it means.
/// </summary>
/// <remarks>
/// <para>
/// This is a marker: it has no members, because everything in it belongs to a format rather than to
/// the pipeline. A package declares a concrete options type - <c>XPlatSvgOptions</c>,
/// <c>XPlatGifOptions</c> - and overloads its factory methods to take it, so the typed spelling is
/// discoverable while core stays ignorant of the type.
/// </para>
/// <para>
/// <b>Options are consulted only by the provider that claims the bytes.</b> A provider that declines
/// never sees them, so a raster provider being handed GIF options alongside a PNG has nothing to
/// care about. The provider that does claim the bytes is the one that was configured, and a claimed
/// load with options it does not understand is a failure rather than a silent no-op: silently
/// ignoring a setting the caller took the trouble to pass is how "my option does nothing" bug
/// reports happen.
/// </para>
/// </remarks>
public interface IXPlatImageFormatOptions
{
}

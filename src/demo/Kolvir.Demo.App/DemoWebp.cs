namespace Kolvir.Demo;

/// <summary>
/// The animated WebP the sample plays, as the file's own bytes.
/// </summary>
/// <remarks>
/// <para>
/// A string rather than a file for the same reason <see cref="DemoIcons"/> is: the render path has
/// to work from an arbitrary working directory, and a viewer of the sample can read the artwork next
/// to the page that draws it.
/// </para>
/// <para>
/// It is a 48x48, twelve-frame animated WebP that declares an infinite loop and 80 ms per frame. The
/// first frame is the full blue canvas; the eleven after it are 8x8 sub-rectangles moving left to
/// right, which is what makes the page show the compositor drawing at an offset rather than a decoder
/// that only ever returns a whole canvas.
/// </para>
/// </remarks>
internal static class DemoWebp
{
    /// <summary>The 48x48 twelve-frame spinner, base64 encoded.</summary>
    internal const string Spinner =
        "UklGRqYCAABXRUJQVlA4WAoAAAACAAAALwAALwAAQU5JTQYAAAAAAAAAAABBTk1GKAAAAAAAAAAAAC8AAC8AAFAAAABWUDhMDwAA" +
        "AC8vwAsABxDR//4HIqL/AQBBTk1GLgAAAAAAAAoAAAcAAAcAAFAAAAJWUDhMFQAAAC8HwAEAD/A/wPmf//mPNMxARP9jAABBTk1G" +
        "LgAAAAIAAAoAAAcAAAcAAFAAAAJWUDhMFQAAAC8HwAEAD/A/wPmf//mPNMxARP9jAABBTk1GLgAAAAQAAAoAAAcAAAcAAFAAAAJW" +
        "UDhMFQAAAC8HwAEAD/A/wPmf//mPNMxARP9jAABBTk1GLgAAAAYAAAoAAAcAAAcAAFAAAAJWUDhMFQAAAC8HwAEAD/A/wPmf//mP" +
        "NMxARP9jAABBTk1GLgAAAAgAAAoAAAcAAAcAAFAAAAJWUDhMFQAAAC8HwAEAD/A/wPmf//mPNMxARP9jAABBTk1GLgAAAAoAAAoA" +
        "AAcAAAcAAFAAAAJWUDhMFQAAAC8HwAEAD/A/wPmf//mPNMxARP9jAABBTk1GLgAAAAwAAAoAAAcAAAcAAFAAAAJWUDhMFQAAAC8H" +
        "wAEAD/A/wPmf//mPNMxARP9jAABBTk1GLgAAAA4AAAoAAAcAAAcAAFAAAAJWUDhMFQAAAC8HwAEAD/A/wPmf//mPNMxARP9jAABB" +
        "Tk1GLgAAABAAAAoAAAcAAAcAAFAAAAJWUDhMFQAAAC8HwAEAD/A/wPmf//mPNMxARP9jAABBTk1GLgAAABIAAAoAAAcAAAcAAFAA" +
        "AAJWUDhMFQAAAC8HwAEAD/A/wPmf//mPNMxARP9jAABBTk1GLgAAABQAAAoAAAcAAAcAAFAAAAJWUDhMFQAAAC8HwAEAD/A/wPmf" +
        "//mPNMxARP9jAAA=";
}

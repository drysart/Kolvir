namespace Kolvir.Demo;

/// <summary>
/// The face the sample carries inside its own assembly: IBM Plex Sans Regular, read once.
/// </summary>
/// <remarks>
/// IBM Plex Sans is licensed under the SIL Open Font License 1.1; the licence text is
/// <c>Kolvir.Demo.App/Fonts/OFL.txt</c>. The face is deliberately one the test machines do not have
/// installed, so a measurement taken with it can only have come from these bytes - which is what the
/// font page and the test suite's <c>FontLoadingTests</c> rely on.
/// </remarks>
internal static class DemoFont
{
    /// <summary>The resource name <c>Kolvir.Demo.App.csproj</c> gives the embedded face.</summary>
    internal const string ResourceName = "Kolvir.Demo.App.Fonts.IBMPlexSans-Regular.ttf";

    /// <summary>The same face as a WOFF 2 file: the container a web font downloads in.</summary>
    internal const string WoffResourceName = "Kolvir.Demo.App.Fonts.IBMPlexSans-Regular.woff2";

    private static byte[]? _data;
    private static byte[]? _woff;

    /// <summary>
    /// The family name the bundled face is registered under: the name recorded inside the file, so
    /// text that draws with it reports the same family it always has.
    /// </summary>
    internal const string Family = "IBM Plex Sans";

    /// <summary>The face's bytes, read from the assembly's resources on first use.</summary>
    internal static byte[] Data => _data ??= Read(ResourceName);

    /// <summary>
    /// Registers the bundled face under <see cref="Family"/> and returns the name, so a page can
    /// build a <see cref="Font"/> at whatever size it needs. Registering twice is idempotent.
    /// </summary>
    /// <returns>The family name to pass to a <see cref="Font"/>.</returns>
    internal static string Register() => FontRegistry.Current.Register(Family, Data);

    /// <summary>
    /// The same face's bytes as a WOFF 2 file, which the sample registers through the application's
    /// <see cref="IFontLoader"/> rather than by handing raw OpenType to the registry.
    /// </summary>
    /// <remarks>
    /// It is 63 KB against the 200 KB face above, which is the whole reason web fonts exist. It is
    /// byte-identical to the file IBM ships for this release - the container, not the glyphs, is what
    /// changed.
    /// </remarks>
    internal static byte[] WoffData => _woff ??= Read(WoffResourceName);

    private static byte[] Read(string resourceName)
    {
        using var resource = typeof(DemoFont).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The demo's embedded font '{resourceName}' is missing.");

        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }
}

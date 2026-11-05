namespace XPlatUI.Demo;

/// <summary>
/// The face the sample carries inside its own assembly: IBM Plex Sans Regular, read once.
/// </summary>
/// <remarks>
/// IBM Plex Sans is licensed under the SIL Open Font License 1.1; the licence text is
/// <c>XPlatUIDemo.App/Fonts/OFL.txt</c>. The face is deliberately one the test machines do not have
/// installed, so a measurement taken with it can only have come from these bytes - which is what the
/// font page and <c>--font-check</c> rely on.
/// </remarks>
internal static class DemoFont
{
    /// <summary>The resource name <c>XPlatUIDemo.App.csproj</c> gives the embedded face.</summary>
    internal const string ResourceName = "XPlatUI.Demo.App.Fonts.IBMPlexSans-Regular.ttf";

    private static byte[]? _data;

    /// <summary>The face's bytes, read from the assembly's resources on first use.</summary>
    internal static byte[] Data => _data ??= Read();

    private static byte[] Read()
    {
        using var resource = typeof(DemoFont).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The demo's embedded font '{ResourceName}' is missing.");

        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }
}

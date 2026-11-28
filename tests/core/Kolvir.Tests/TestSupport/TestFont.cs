using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The one face every test that depends on measurement uses: IBM Plex Sans Regular, embedded from
/// the demo application's own copy.
/// </summary>
/// <remarks>
/// <para>
/// The face is deliberately one the runners do not have installed, so a width measured with it can
/// only have come from these bytes. That is what makes a wrap point, a caret position or an
/// auto-sized control's width the same number on Windows, macOS and Linux.
/// </para>
/// <para>
/// The bytes are read once per process and registered under <see cref="Family"/>, so a test builds a
/// font with <c>new Font(TestFont.Family, size)</c> and gets the same face at every size. Nothing is
/// disposable; the theme is only changed through <see cref="Pin"/>, which every test can dispose to
/// get the platform's default face back.
/// </para>
/// </remarks>
internal static class TestFont
{
    /// <summary>The resource name <c>Kolvir.Tests.csproj</c> gives the embedded face.</summary>
    internal const string ResourceName = "Kolvir.Tests.Fonts.IBMPlexSans-Regular.ttf";

    private static byte[]? _data;

    /// <summary>The family name the embedded face is registered under.</summary>
    internal const string Family = "IBM Plex Sans";

    /// <summary>The face's bytes.</summary>
    internal static byte[] Data => _data ??= Read();

    /// <summary>
    /// Registers the embedded face under <see cref="Family"/> and returns the name, so a test can
    /// build a font at whatever size it needs. Registering twice is idempotent.
    /// </summary>
    /// <returns>The family name to pass to a <see cref="Font"/>.</returns>
    internal static string Register() => FontRegistry.Current.Register(Family, Data);

    /// <summary>
    /// Makes the embedded face the application's font for the lifetime of the returned value, and
    /// restores the platform default face afterwards.
    /// </summary>
    internal static IDisposable Pin()
    {
        Theme.FontFamily = Register();
        return new Restore();
    }

    private static byte[] Read()
    {
        using var resource = typeof(TestFont).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The test suite's embedded font '{ResourceName}' is missing.");

        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }

    private sealed class Restore : IDisposable
    {
        public void Dispose() => Theme.FontFamily = null;
    }
}

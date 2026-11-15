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
/// The bytes are read once per process and handed to <see cref="Font.FromBytes"/> as often as a
/// test needs a font; the theme itself is only changed through <see cref="Pin"/>, which every test
/// can dispose to get the platform's default face back.
/// </para>
/// </remarks>
internal static class TestFont
{
    /// <summary>The resource name <c>Kolvir.Tests.csproj</c> gives the embedded face.</summary>
    internal const string ResourceName = "Kolvir.Tests.Fonts.IBMPlexSans-Regular.ttf";

    private static byte[]? _data;

    /// <summary>The face's bytes.</summary>
    internal static byte[] Data => _data ??= Read();

    /// <summary>A font at <paramref name="size"/> points from the embedded face.</summary>
    internal static Font Create(float size = 13f) => Font.FromBytes(Data, size);

    /// <summary>
    /// Makes the embedded face the application's font for the lifetime of the returned value, and
    /// restores the platform default face afterwards.
    /// </summary>
    internal static IDisposable Pin()
    {
        Theme.UseFont(Create());
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

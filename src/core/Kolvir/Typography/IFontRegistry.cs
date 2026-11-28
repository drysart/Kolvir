namespace Kolvir;

/// <summary>
/// The faces an application supplied, named so a <see cref="Font"/> can refer to them as if the
/// operating system provided them.
/// </summary>
/// <remarks>
/// <para>
/// <b>A font is a description, so a face has to be named to be used.</b> Registering one gives it a
/// family name; a description naming that family resolves to it everywhere - in a control's font, in
/// the theme, in a rich label's span - and on every platform. A registered name wins over a system
/// family of the same name, which is what lets an application shadow "Segoe UI" with its own face
/// without changing a single <see cref="Font"/> in its code.
/// </para>
/// <para>
/// <b>Registration is legal at any time.</b> Registering or unregistering a family invalidates every
/// resolution of it and tells the theme that the fonts changed, so a live tree re-measures and
/// repaints rather than keeping layout measured against the old face. An existing realization keeps
/// the face it already holds and stays valid and shareable until the last font that realized it is
/// gone; what changes is what resolves from then on.
/// </para>
/// <para>
/// <b>Where the instance comes from.</b> <see cref="FontRegistry.Current"/> is one lazily created
/// registry for the life of the process, and is what the application's container registers. It is a
/// static rather than only a service because realization happens with no control in sight: the theme
/// fonts are realized by whatever measures text first, which can be a backend's own chrome before a
/// window exists. An empty registry costs one dictionary probe on the realization path and nothing
/// else, so an application that registers nothing pays nothing.
/// </para>
/// </remarks>
public interface IFontRegistry
{
    /// <summary>Registers font data under a family name.</summary>
    /// <param name="family">The name the family is resolved by. Null, empty or whitespace is
    /// rejected.</param>
    /// <param name="data">The bytes of a <c>.ttf</c> or <c>.otf</c>, or of a container the
    /// application's <see cref="IFontLoader"/> can unwrap.</param>
    /// <returns>The family name, which is <paramref name="family"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="family"/> is null, empty or whitespace.</exception>
    /// <exception cref="FontFormatException"><paramref name="data"/> is not a font this build can read.</exception>
    string Register(string family, ReadOnlySpan<byte> data);

    /// <summary>Registers a face that was already read from data.</summary>
    /// <param name="family">The name the family is resolved by. Null, empty or whitespace is
    /// rejected.</param>
    /// <param name="face">The face to register.</param>
    /// <returns>The family name, which is <paramref name="family"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="face"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="family"/> is null, empty or whitespace.</exception>
    /// <remarks>
    /// A second face under the same family with a different weight or slant is a second face of that
    /// family, which is how an application gets real emphasis from its own files. The same weight and
    /// slant registered twice replaces the earlier face.
    /// </remarks>
    string Register(string family, FontFace face);

    /// <summary>Reads a font file and registers it under a family name.</summary>
    /// <param name="path">The file to read, in any format the application's loader can unwrap.</param>
    /// <param name="family">The name to register it under, or null to use the family name recorded
    /// inside the file.</param>
    /// <returns>The family name the face was registered under.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty or whitespace.</exception>
    /// <exception cref="System.IO.FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="FontFormatException">The file is not a font this build can read.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="family"/> was given and is empty, or was omitted and the file records no
    /// family name.
    /// </exception>
    /// <remarks>
    /// This is the one-liner an application wants at start-up:
    /// <c>Theme.FontFamily = registry.RegisterFromFile("Fonts/Inter.woff2");</c>. The format is the
    /// loader's business, so a <c>.ttf</c> and a <c>.woff2</c> take the same line once the matching
    /// feature is enabled.
    /// </remarks>
    string RegisterFromFile(string path, string? family = null);

    /// <summary>Removes every face registered under a family name.</summary>
    /// <param name="family">The family to remove, or null to do nothing.</param>
    /// <returns>True when something was registered under that name, false otherwise.</returns>
    /// <remarks>
    /// <para>
    /// It never throws, and that asymmetry with <see cref="Register(string, FontFace)"/> is deliberate:
    /// this is the cleanup half, and cleanup runs in <c>finally</c> blocks and error paths where the
    /// name may never have been produced. A throw there would mask the failure that got it there.
    /// </para>
    /// <para>
    /// It is idempotent: a name that was never registered, one already removed, and null all answer
    /// false and do nothing, including no invalidation when nothing changed. Fonts already realized
    /// from the family keep their face until they look again; fonts realized afterwards resolve the
    /// way they would have if the family had never been registered.
    /// </para>
    /// </remarks>
    bool Unregister(string? family);

    /// <summary>Every registered family and the faces it has.</summary>
    /// <remarks>
    /// A snapshot taken when it is read, not a live view, so a caller can hold it while it works.
    /// </remarks>
    IReadOnlyList<RegisteredFontFamily> Families { get; }
}

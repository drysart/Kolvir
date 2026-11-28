namespace Kolvir;

/// <summary>
/// The one registry of application-supplied faces for the process.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Current"/> is a lazy static and the instance the application's container registers, so
/// a face registered before an application started and one registered through an injected
/// <see cref="IFontRegistry"/> are the same face under the same name. A registry that lived only in
/// the container could not serve the theme fonts, which are realized with no control and therefore no
/// services in sight.
/// </para>
/// <para>
/// Registration and unregistration are UI-thread operations and start-up shaped: they run once per
/// family, never per frame. The lock here is what a realization pays to look a family up, once per
/// description in use, and nothing on the measure path.
/// </para>
/// </remarks>
public sealed class FontRegistry : IFontRegistry
{
    /// <summary>
    /// The sentence a failure carries when there was no installed loader to read with.
    /// </summary>
    /// <remarks>
    /// Without it, a container registered before the application existed reports only "the data is not
    /// a font this build can read", and the two things a caller needs are missing: that the built-in
    /// loader reads TrueType and OpenType and nothing else, and that a format provider reaches it only
    /// through <see cref="Application"/>. Both fixes are named, because which one is right depends on
    /// what the caller is holding.
    /// </remarks>
    private const string NoLoaderExplanation =
        "No Application has been constructed yet, so no font format provider was installed and only " +
        "TrueType and OpenType can be read: a container format such as WOFF or WOFF 2 needs the " +
        "provider its feature adds - AddWoffFonts() in ApplicationOptions.ConfigureServices - which is " +
        "composed into the loader when the Application is constructed. Construct the Application before " +
        "registering a container, or register the OpenType bytes it unwraps to.";

    /// <summary>
    /// What a registration reads with when no application has installed a loader: the built-in
    /// OpenType path and no format providers.
    /// </summary>
    private static readonly FontLoader FallbackLoader = new();

    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<(int Weight, bool Italic), FontFace>> _families =
        new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    private IFontLoader? _loader;

    private FontRegistry()
    {
    }

    /// <summary>The registry for this process, which the application's container also registers.</summary>
    public static FontRegistry Current { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<RegisteredFontFamily> Families
    {
        get
        {
            lock (_gate)
            {
                var families = new List<RegisteredFontFamily>(_order.Count);
                foreach (var name in _order)
                {
                    var faces = new List<FontFace>(_families[name].Count);
                    faces.AddRange(_families[name].Values);
                    families.Add(new RegisteredFontFamily(name, faces));
                }

                return families;
            }
        }
    }

    /// <inheritdoc />
    public string Register(string family, ReadOnlySpan<byte> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        return Register(family, LoadFace(data));
    }

    /// <inheritdoc />
    public string Register(string family, FontFace face)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentNullException.ThrowIfNull(face);

        lock (_gate)
        {
            if (!_families.TryGetValue(family, out var faces))
            {
                faces = [];
                _families[family] = faces;
                _order.Add(family);
            }

            faces[(face.Weight, face.Italic)] = face;
        }

        FontManager.InvalidateFamily(family);
        return family;
    }

    /// <inheritdoc />
    public string RegisterFromFile(string path, string? family = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var face = LoadFace(File.ReadAllBytes(path));
        return Register(string.IsNullOrWhiteSpace(family) ? face.FamilyName : family, face);
    }

    /// <inheritdoc />
    public bool Unregister(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return false;
        }

        bool removed;
        lock (_gate)
        {
            removed = _families.Remove(family);
            if (removed)
            {
                _order.Remove(family);
            }
        }

        if (removed)
        {
            FontManager.InvalidateFamily(family);
        }

        return removed;
    }

    /// <summary>Installs the loader that registered files are read through.</summary>
    /// <param name="loader">The application's loader, which carries its format providers.</param>
    /// <remarks>
    /// <see cref="Application"/> installs its container's <see cref="IFontLoader"/>, which is what makes
    /// a registered <c>.woff2</c> work: the format providers are composed from the features the
    /// application declared, and the registry is a static that has no container of its own. Until one is
    /// installed - and after it is cleared again - a registration reads through
    /// <see cref="FallbackLoader"/> and a failure says why.
    /// </remarks>
    internal void UseLoader(IFontLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        Volatile.Write(ref _loader, loader);
    }

    /// <summary>Drops the installed loader, so registrations read through the built-in one again.</summary>
    /// <remarks>
    /// Called when the application that installed it is torn down. The loader itself is not disposable
    /// and would still work, but a registration made after the application is gone has no features
    /// behind it, and saying so is better than resolving through a container that no longer exists.
    /// </remarks>
    internal void ClearLoader() => Volatile.Write(ref _loader, null);

    /// <summary>
    /// Reads a face from encoded data, explaining a container nothing could read when there was no
    /// application to install a provider.
    /// </summary>
    /// <param name="data">The encoded font.</param>
    /// <returns>The face the data holds.</returns>
    /// <remarks>
    /// The explanation is added only for the two WOFF signatures, which are the containers that reach
    /// this fallback without being obviously broken: with no application there is no provider to
    /// recognise them, so "this is not a font" is true and useless. For anything else - a truncated
    /// OpenType file, a file that is not a font at all - the loader's own message is already the whole
    /// answer and this adds nothing.
    /// </remarks>
    private FontFace LoadFace(ReadOnlySpan<byte> data)
    {
        if (Volatile.Read(ref _loader) is { } installed)
        {
            return installed.LoadFromBytes(data);
        }

        try
        {
            return FallbackLoader.LoadFromBytes(data);
        }
        catch (FontFormatException exception) when (exception.ProviderName.Length == 0 && IsContainer(data))
        {
            throw new FontFormatException(null, "data", exception, NoLoaderExplanation);
        }
    }

    /// <summary>True when the data starts with a WOFF or WOFF 2 signature.</summary>
    /// <param name="data">The data a load was handed.</param>
    /// <returns>True when the data is one of the two containers.</returns>
    private static bool IsContainer(ReadOnlySpan<byte> data) =>
        data.Length >= 4 &&
        data[0] == (byte)'w' &&
        data[1] == (byte)'O' &&
        data[2] == (byte)'F' &&
        (data[3] == (byte)'F' || data[3] == (byte)'2');

    /// <summary>
    /// The face a description resolves to, or false when the family is not registered.
    /// </summary>
    /// <param name="family">The normalised family name; the empty string is never registered.</param>
    /// <param name="weight">The weight asked for.</param>
    /// <param name="italic">True when an italic face was asked for.</param>
    /// <param name="face">The chosen face, when the family is registered.</param>
    /// <returns>True when <paramref name="face"/> holds a face.</returns>
    /// <remarks>
    /// Within a family the request is answered with the nearest weight, preferring a matching slant,
    /// and a family with only a regular face serves every request with it. Nothing is synthesised,
    /// which is the same rule the platform's families follow.
    /// </remarks>
    internal bool TryResolve(string family, int weight, bool italic, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out FontFace? face)
    {
        face = null;

        if (family.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_families.TryGetValue(family, out var faces) || faces.Count == 0)
            {
                return false;
            }

            int bestSlantPenalty = int.MaxValue;
            int bestWeightDistance = int.MaxValue;

            foreach (var entry in faces)
            {
                int slantPenalty = entry.Key.Italic == italic ? 0 : 1;
                int distance = Math.Abs(entry.Key.Weight - weight);

                if (slantPenalty < bestSlantPenalty ||
                    (slantPenalty == bestSlantPenalty && distance < bestWeightDistance))
                {
                    bestSlantPenalty = slantPenalty;
                    bestWeightDistance = distance;
                    face = entry.Value;
                }
            }

            return face is not null;
        }
    }
}

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
        return Register(family, Loader.LoadFromBytes(data));
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

        var face = Loader.LoadFromPath(path);
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

    /// <summary>
    /// The loader a registered file is read through, or a bare one for the built-in OpenType path.
    /// </summary>
    /// <remarks>
    /// <see cref="Application"/> installs its container's <see cref="IFontLoader"/> here, which is what
    /// makes a registered <c>.woff2</c> work: the format providers are composed from the features the
    /// application declared, and the registry is a static that has no container of its own. Without an
    /// application - a test, say - the built-in loader reads OpenType and nothing else.
    /// </remarks>
    private IFontLoader Loader => Volatile.Read(ref _loader) ?? new FontLoader();

    /// <summary>Installs the loader that registered files are read through.</summary>
    /// <param name="loader">The application's loader, which carries its format providers.</param>
    internal void UseLoader(IFontLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        Volatile.Write(ref _loader, loader);
    }

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

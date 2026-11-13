namespace Kolvir;

/// <summary>
/// An <see cref="IFontLoader"/> built from a fixed list of format providers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registration is a startup concern, so there is no registration.</b> A loader is composed from an
/// explicit list - by the service container when an application declares its features, or by hand with
/// <c>new FontLoader(providers)</c> - and that list is the loader's for its lifetime. There is no
/// ambient registry to add to, nothing to unregister, and no lock around a list that cannot change:
/// two applications in one process, and two tests in one process, cannot see each other's formats.
/// </para>
/// <para>
/// <b>The rules a load follows, in the order they matter:</b>
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Providers are consulted before the built-in loader.</b> A registered provider that claims the
/// data produces the font, so registering a WOFF provider is what makes WOFF work at all. The
/// built-in loader is fallback behaviour for data nobody claims, not an entry in the list with a
/// priority.
/// </item>
/// <item>
/// <b>Two providers claim the same data.</b> The higher <see cref="IFontFormatProvider.Priority"/>
/// wins; at equal priority the later registration wins - the provider further along the list the loader
/// was built from - so an application can replace a provider by registering another at the same
/// priority.
/// </item>
/// <item>
/// <b>A claiming provider fails.</b> The load throws a <see cref="FontFormatException"/> naming the
/// provider and does not fall through to the next claim or to the built-in loader: the container was
/// already identified, and handing it to something else is how a damaged file gets read anyway.
/// </item>
/// <item>
/// <b>A claiming provider returns something that is not a font.</b> That is the provider's failure too,
/// reported the same way, for the same reason: it said the data was its format.
/// </item>
/// <item>
/// <b>No provider claims the data, and the built-in loader cannot read it.</b> The load throws a
/// <see cref="FontFormatException"/> with no provider name, which is the one failure a caller has to
/// handle: a font that is not going to be shown. It is the same failure <see cref="Font.FromBytes"/>
/// throws, because it is the same answer.
/// </item>
/// <item>
/// <b>A provider throws while being asked whether it claims the data.</b> It has answered "not mine":
/// the exception is swallowed and the next provider is asked, because identifying a stream must not be
/// able to fail a load.
/// </item>
/// <item>
/// <b>What the built-in loader reads.</b> TrueType and OpenType, the same as
/// <see cref="Font.FromBytes"/>: those are the formats Skia parses, and this loader does not add any.
/// </item>
/// </list>
/// <para>
/// <b>Where a font comes from.</b> An application resolves <see cref="IFontLoader"/> from its services -
/// the application registers one, composed from every format the application declared - and assigns
/// the result where it wants the face. There is no static entry point that consults an ambient list,
/// which is what makes "what can this application read" a question with exactly one answer.
/// </para>
/// </remarks>
public sealed class FontLoader : IFontLoader
{
    /// <summary>How many bytes of data a provider is shown when asked whether it recognises it.</summary>
    /// <remarks>
    /// The magic number of every container in use is in the first four bytes, so this is generous. It
    /// matches <see cref="ImageDataFactory.HeaderLength"/> so the two seams read the same way.
    /// </remarks>
    public const int HeaderLength = 512;

    /// <summary>The size a load uses when the caller does not choose one: the theme's default of 13.</summary>
    public const float DefaultSize = 13f;

    private readonly IFontFormatProvider[] _providers;

    /// <summary>
    /// Creates a loader with no providers: loads through the built-in Skia path only.
    /// </summary>
    public FontLoader()
        : this([])
    {
    }

    /// <summary>Creates a loader from a fixed list of providers.</summary>
    /// <param name="providers">
    /// The formats this loader knows. The list is copied and sorted once; later changes to the
    /// collection passed in have no effect.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="providers"/> or one of its entries is null.</exception>
    /// <exception cref="ArgumentException">A provider reports no name.</exception>
    /// <remarks>
    /// Sorting is by priority and then by the order given, so "later registration wins at equal
    /// priority" survives the sort. Keeping the pairs together is why the entries are copied as a
    /// record of both rather than sorting the providers themselves.
    /// </remarks>
    public FontLoader(IEnumerable<IFontFormatProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var list = providers.ToArray();

        foreach (var provider in list)
        {
            ArgumentNullException.ThrowIfNull(provider);

            if (string.IsNullOrWhiteSpace(provider.Name))
            {
                throw new ArgumentException(
                    $"A {nameof(IFontFormatProvider)} reports no {nameof(IFontFormatProvider.Name)}.",
                    nameof(providers));
            }
        }

        _providers = list
            .Select((provider, index) => (Provider: provider, Index: index))
            .OrderByDescending(entry => entry.Provider.Priority)
            .ThenByDescending(entry => entry.Index)
            .Select(entry => entry.Provider)
            .ToArray();
    }

    /// <summary>The formats this loader consults, in the order it consults them.</summary>
    /// <remarks>
    /// Exposed for diagnostics - a log line about "what can this application read" - rather than for
    /// editing: the list is fixed, so this is a copy made once rather than a live view.
    /// </remarks>
    public IReadOnlyList<IFontFormatProvider> Providers => _providers;

    /// <inheritdoc />
    public Font LoadFromPath(string path, float size = DefaultSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return LoadFromBytes(File.ReadAllBytes(path), size);
    }

    /// <inheritdoc />
    public Font LoadFromStream(Stream stream, float size = DefaultSize)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // The whole face is copied rather than streamed, for the same reason Font.FromStream copies:
        // the caller is free to close the stream as soon as this returns, and a provider wants the
        // whole thing anyway.
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return LoadFromBytes(buffer.ToArray(), size);
    }

    /// <inheritdoc />
    public Font LoadFromBytes(ReadOnlySpan<byte> data, float size = DefaultSize)
    {
        if (data.IsEmpty)
        {
            throw new FontFormatException(null, "data");
        }

        if (Claim(data) is { } provider)
        {
            ReadOnlyMemory<byte> decoded;
            try
            {
                decoded = provider.Decode(data.ToArray());
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new FontFormatException(provider.Name, "data", exception);
            }

            if (decoded.IsEmpty)
            {
                throw new FontFormatException(provider.Name, "data");
            }

            try
            {
                return Font.FromBytes(decoded.Span, size);
            }
            catch (FontFormatException exception)
            {
                // The provider said the data was its format and then handed back something Skia will
                // not read, which is the provider's failure rather than the caller's.
                throw new FontFormatException(provider.Name, "data", exception);
            }
        }

        // Nobody claimed it: the built-in path, which is the loader the library has always had. Its
        // failure is already the right one, down to the parameter name.
        return Font.FromBytes(data, size);
    }

    /// <summary>
    /// The provider that claims the data, or null when none does.
    /// </summary>
    /// <remarks>
    /// A provider that throws while being asked is treated as having declined, so identifying a stream
    /// cannot fail a load. The list is already in precedence order, so the first answer wins.
    /// </remarks>
    private IFontFormatProvider? Claim(ReadOnlySpan<byte> data)
    {
        var header = data.Length <= HeaderLength ? data : data[..HeaderLength];

        foreach (var provider in _providers)
        {
            try
            {
                if (provider.CanLoad(header))
                {
                    return provider;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // "Not mine", by another name.
            }
        }

        return null;
    }
}

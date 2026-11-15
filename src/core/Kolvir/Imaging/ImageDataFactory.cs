using SkiaSharp;

namespace Kolvir;
/// <summary>
/// An <see cref="IImageDataFactory"/> built from a fixed list of format providers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registration is a startup concern, so there is no registration.</b> A factory is composed from
/// an explicit list - by the service container when an application declares its features, or by hand
/// with <c>new ImageDataFactory(providers)</c> - and that list is the factory's for its lifetime.
/// There is no ambient registry to add to, nothing to unregister, and no lock around a list that
/// cannot change: two applications in one process, and two tests in one process, cannot see each
/// other's formats.
/// </para>
/// <para>
/// <b>The rules a load follows, in the order they matter:</b>
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Providers are consulted before the built-in decoder.</b> A registered provider that claims the
/// bytes produces the image, so registering an animated GIF provider takes GIF away from the still
/// decoder that reads its first frame. The built-in decoder is fallback behaviour for bytes nobody
/// claims, not an entry in the list with a priority.
/// </item>
/// <item>
/// <b>Two providers claim the same bytes.</b> The higher <see cref="IImageFormatProvider.Priority"/>
/// wins; at equal priority the later registration wins - the provider further along the list the
/// factory was built from - so an application can replace a provider by registering another at the
/// same priority, and the last one declared is the one that answers.
/// </item>
/// <item>
/// <b>A claiming provider fails.</b> The load throws an <see cref="ImageFormatException"/> naming
/// the provider and does not fall through to the next claim: the bytes were already identified, and
/// handing them to a different parser is how a refused file gets read anyway.
/// </item>
/// <item>
/// <b>No provider claims the bytes, and the built-in decoder cannot read them.</b> The load throws an
/// <see cref="ImageFormatException"/> with no provider name, which is the one failure a caller
/// has to handle: assign nothing, show nothing, log it - there is no control to report to, because
/// the control does not load.
/// </item>
/// <item>
/// <b>A provider throws while being asked whether it claims the bytes.</b> It has answered "not
/// mine": the exception is swallowed and the next provider is asked, because identifying a stream
/// must not be able to fail a load.
/// </item>
/// <item>
/// <b>What the built-in decoder reads.</b> PNG, JPEG, WebP, GIF (its first frame), BMP and ICO. That
/// is the same list the README states, and a GIF loaded with no animated-GIF provider registered is
/// a still first frame - deliberately, and not as an error.
/// </item>
/// <item>
/// <b>Data outliving the factory.</b> A load produces self-contained data: it owns fully decoded
/// pixels, not a stream or a reference to the factory, so disposing the container - or dropping the
/// factory - does not disturb an image that came out of it. A factory holds no unmanaged resources
/// of its own and is not disposable.
/// </item>
/// </list>
/// <para>
/// <b>Streams that cannot seek.</b> A provider is promised a seekable stream, because it has to look
/// at the start twice: once to be recognised and once to be decoded. A pipe, a network stream or a
/// compressed stream is buffered here, bounded by <see cref="BufferLimit"/>, rather than refused:
/// refusing would turn the one case a real application hits into an exception nobody expects. The
/// bound is what stops a hostile stream from becoming an out-of-memory failure.
/// </para>
/// <para>
/// <b>Where a control gets its image.</b> It does not: an application resolves
/// <see cref="IImageDataFactory"/> from its services - the application registers one, composed
/// from every format the application declared - and assigns
/// <c>image.Source = factory.LoadFrom...</c>. An application without a container composes a factory of
/// its own. The control has no loader and no fallback of its own, which is what makes "what can this
/// application read" a question with exactly one answer.
/// </para>
/// </remarks>
public sealed class ImageDataFactory : IImageDataFactory
{
    /// <summary>How many bytes of a stream a provider is shown when asked whether it recognises it.</summary>
    /// <remarks>
    /// 512 is enough for every magic number in use plus the XML prolog and root element of a vector
    /// document that carries comments or a DOCTYPE ahead of it.
    /// </remarks>
    public const int HeaderLength = 512;

    /// <summary>
    /// How large an unseekable stream may be before it is refused, in bytes: 64 MiB.
    /// </summary>
    /// <remarks>
    /// Buffering is bounded because the alternative is a stream that grows the process until it dies,
    /// and a stream larger than this that cannot seek is more likely to be a mistake than an image. A
    /// larger one can still be read by copying it to a file or a <see cref="MemoryStream"/> first,
    /// which makes it seekable and skips the buffer entirely.
    /// </remarks>
    public const long DefaultBufferLimit = 64L * 1024 * 1024;

    private readonly IImageFormatProvider[] _providers;

    /// <summary>
    /// Creates a factory with no providers: loads through the built-in raster decoder only.
    /// </summary>
    public ImageDataFactory()
        : this([])
    {
    }

    /// <summary>Creates a factory from a fixed list of providers.</summary>
    /// <param name="providers">
    /// The formats this factory knows. The list is copied and sorted once; later changes to the
    /// collection passed in have no effect.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="providers"/> or one of its entries is null.</exception>
    /// <exception cref="ArgumentException">A provider reports no name.</exception>
    public ImageDataFactory(IEnumerable<IImageFormatProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var list = providers.ToArray();

        foreach (var provider in list)
        {
            ArgumentNullException.ThrowIfNull(provider);

            if (string.IsNullOrWhiteSpace(provider.Name))
            {
                throw new ArgumentException("An image format provider must report a name.", nameof(providers));
            }
        }

        // Priority first, then the reverse of the order the list arrived in: the later registration
        // wins a tie, which is the rule the registry this replaced documented. OrderByDescending is a
        // stable sort, so providers that tie on both keys keep their relative order.
        _providers = list
            .Select((provider, index) => (provider, index))
            .OrderByDescending(entry => entry.provider.Priority)
            .ThenByDescending(entry => entry.index)
            .Select(entry => entry.provider)
            .ToArray();
    }

    /// <summary>The providers this factory consults, in the order it consults them.</summary>
    public IReadOnlyList<IImageFormatProvider> Providers => _providers;

    /// <summary>
    /// The size limit for an unseekable stream, in bytes. Defaults to
    /// <see cref="DefaultBufferLimit"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long BufferLimit
    {
        get => _bufferLimit;
        init => _bufferLimit = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The limit must be positive.");
    }

    private readonly long _bufferLimit = DefaultBufferLimit;

    /// <inheritdoc />
    public ImageData LoadFromPath(string path, IImageFormatOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        // The file's own failures are its own: a missing file is not a format problem, so IOException,
        // UnauthorizedAccessException and ArgumentException are left as they are rather than turned
        // into an ImageFormatException the caller would have to unwrap.
        using var stream = File.OpenRead(path);
        return LoadFromStream(stream, Path.GetExtension(path), options);
    }

    /// <inheritdoc />
    public ImageData LoadFromStream(Stream stream, IImageFormatOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return LoadFromStream(stream, extensionHint: null, options);
    }

    /// <inheritdoc />
    public ImageData LoadFromBytes(ReadOnlySpan<byte> bytes, IImageFormatOptions? options = null)
    {
        // Not a copy in the sense that matters - the decode happens inside this call - but
        // MemoryStream needs one, and a decoded image is larger than its file anyway.
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        return LoadFromStream(stream, extensionHint: null, options);
    }

    private ImageData LoadFromStream(Stream stream, string? extensionHint, IImageFormatOptions? options)
    {
        Stream source = stream;
        MemoryStream? buffered = null;

        try
        {
            if (!stream.CanSeek)
            {
                buffered = new MemoryStream();
                CopyBounded(stream, buffered);
                buffered.Position = 0;
                source = buffered;
            }

            source.Position = 0;

            var header = ReadHeader(source);
            var provider = Find(header);

            if (provider is not null)
            {
                ImageData? data;
                try
                {
                    source.Position = 0;
                    data = provider.Create(source, Normalise(extensionHint), options);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    throw new ImageFormatException(provider.Name, ex);
                }

                return data ?? throw new ImageFormatException(provider.Name);
            }

            // Nobody claimed the bytes, so the built-in decoder gets its turn. It is fallback
            // behaviour rather than a provider: it has no priority to be outranked by, and there is
            // no way for it to take a format away from a package that claims it. It has nothing to
            // configure, so options that arrive here belong to a format nobody registered.
            if (options is not null)
            {
                throw ImageFormatException.OptionsNotUnderstood(options.GetType());
            }

            source.Position = 0;
            var picture = RasterPicture.Decode(source);
            return picture is null
                ? throw ImageFormatException.Unrecognised()
                : ImageData.FromPicture(picture, disposePictureWithData: true);
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    /// <summary>The first provider in the list that claims these bytes, or null when none does.</summary>
    private IImageFormatProvider? Find(ReadOnlySpan<byte> header)
    {
        foreach (var provider in _providers)
        {
            bool claims;
            try
            {
                claims = provider.CanDecode(header);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A provider that cannot answer has not claimed the bytes. Deciding it here rather
                // than letting the exception out is what keeps a buggy provider from turning the
                // load of a PNG into an exception on the paint thread.
                claims = false;
            }

            if (claims)
            {
                return provider;
            }
        }

        return null;
    }

    private void CopyBounded(Stream from, MemoryStream to)
    {
        var buffer = new byte[81920];
        long total = 0;

        int read;
        while ((read = from.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > BufferLimit)
            {
                throw ImageFormatException.TooLargeToBuffer(BufferLimit);
            }

            to.Write(buffer, 0, read);
        }
    }

    /// <summary>Reads the bytes a provider is shown, leaving the stream where it was.</summary>
    private static ReadOnlySpan<byte> ReadHeader(Stream stream)
    {
        long origin = stream.Position;
        var header = new byte[HeaderLength];
        int read = 0;

        try
        {
            while (read < header.Length)
            {
                int count = stream.Read(header, read, header.Length - read);
                if (count == 0)
                {
                    break;
                }

                read += count;
            }
        }
        finally
        {
            stream.Position = origin;
        }

        return read == header.Length ? header : header.AsSpan(0, read);
    }

    private static string? Normalise(string? extensionHint) =>
        string.IsNullOrEmpty(extensionHint) ? null : extensionHint.TrimStart('.').ToLowerInvariant();
}

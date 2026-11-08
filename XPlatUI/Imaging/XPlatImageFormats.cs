namespace XPlatUI;

/// <summary>
/// The process-wide list of providers <see cref="XPlatImage"/> asks when it is handed bytes.
/// </summary>
/// <remarks>
/// <para>
/// An application or a feature registers a provider here once, and then every
/// <see cref="XPlatImage.Load(Stream)"/> can read that format. Nothing is registered by default
/// except the raster decoder the control has always had, so a toolkit with no feature installed
/// behaves exactly as it did before this seam existed.
/// </para>
/// <para>
/// The rules a load follows, in the order they matter:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Two providers claim the same bytes.</b> The higher
/// <see cref="IXPlatImageFormatProvider.Priority"/> wins; at equal priority the more recently
/// registered wins. Both are deliberate: priority lets a format that is a container of another
/// (an SVG inside a gzip stream, say) outrank a looser matcher, and registration order lets an
/// application replace the built-in decoder or its own earlier provider without a priority scheme.
/// No load ever tries two providers for the same bytes.
/// </item>
/// <item>
/// <b>A claiming provider fails.</b> The load returns false and raises
/// <see cref="XPlatImage.AddLoadFailedHandler"/> with an <see cref="XPlatImageFormatException"/>
/// naming the provider. It does not fall through to the next claim, because the bytes were already
/// identified: handing a document one provider refused to a different parser is how a refused file
/// gets read anyway. The exception a provider threw is the failure's
/// <see cref="Exception.InnerException"/>, so nothing is lost.
/// </item>
/// <item>
/// <b>No provider claims the bytes.</b> The load returns false and raises nothing, which is the
/// contract the control has always had for a stream that is not an image at all.
/// </item>
/// <item>
/// <b>Unregistering while a picture is alive.</b> Supported. A picture a provider returned is a
/// self-contained <see cref="SkiaSharp.SKPicture"/>; removing the provider ends what future loads
/// can do, not what an already-loaded control can draw, and it does not dispose anything. This is
/// what lets a scope own its registration, and it is why a provider that caches pictures may keep
/// them after its own handle is disposed.
/// </item>
/// </list>
/// <para>
/// Registration is not hidden behind a service the application has to resolve. A provider list is
/// process state, like the theme, so the registration is a call that returns a handle; a feature
/// package offers an <c>Add…()</c> service-collection extension that makes the same call, and the
/// handle it keeps there is only so a container can undo it.
/// </para>
/// </remarks>
public static class XPlatImageFormats
{
    /// <summary>
    /// How many bytes of a stream a provider is shown when it is asked whether it recognises it.
    /// </summary>
    /// <remarks>
    /// 512 is enough for every magic number in use plus the XML prolog and root element of a
    /// vector document that carries comments or a DOCTYPE ahead of it.
    /// </remarks>
    internal const int HeaderLength = 512;

    private static readonly object Gate = new();

    // Replaced wholesale rather than mutated, so a load reads a consistent list without holding a
    // lock and a registration cannot be seen half applied.
    private static IXPlatImageFormatProvider[] _providers = [XPlatRasterImageFormatProvider.Shared];

    // How many live handles each provider has. Two calls to Add with the same instance are one entry
    // in the list and two claims on it, so disposing one handle - a nested scope's, say - does not
    // take the provider away from the scope that registered it first.
    private static readonly Dictionary<IXPlatImageFormatProvider, int> Claims = [];

    /// <summary>Registers a provider, replacing it if it is already registered.</summary>
    /// <param name="provider">The provider to register.</param>
    /// <returns>A handle that unregisters it when disposed; disposing twice is a no-op.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The provider reports no name, which would make a failure it causes unattributable.
    /// </exception>
    /// <remarks>
    /// Registering an instance that is already registered moves it to the front of the equal-priority
    /// order rather than adding a duplicate, so an application that registers the same provider
    /// twice behaves as if it did once.
    /// </remarks>
    public static IDisposable Add(IXPlatImageFormatProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(provider.Name))
        {
            throw new ArgumentException("An image format provider must report a name.", nameof(provider));
        }

        lock (Gate)
        {
            var current = _providers;
            if (Array.IndexOf(current, provider) < 0)
            {
                var next = new IXPlatImageFormatProvider[current.Length + 1];
                Array.Copy(current, next, current.Length);
                next[^1] = provider;
                Volatile.Write(ref _providers, next);
                Claims[provider] = 1;
            }
            else
            {
                Claims[provider] = Claims.TryGetValue(provider, out int claims) ? claims + 1 : 1;
            }
        }

        return new Registration(provider);
    }

    /// <summary>Unregisters a provider that <see cref="Add"/> registered.</summary>
    /// <param name="provider">The provider to remove.</param>
    /// <returns>True when it was registered, false when it was not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
    /// <remarks>
    /// The built-in raster decoder cannot be removed, because a control that cannot read a PNG with
    /// nothing installed would be a broken control rather than a configured one.
    /// </remarks>
    public static bool Remove(IXPlatImageFormatProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (Gate)
        {
            // A removal is unconditional, unlike disposing a handle: an application that says
            // "remove this provider" means now, and any handle still open becomes inert.
            Claims.Remove(provider);
            return RemoveCore(provider);
        }
    }

    /// <summary>The provider that claims these bytes, or null when none does.</summary>
    internal static IXPlatImageFormatProvider? Find(ReadOnlySpan<byte> header)
    {
        var providers = Volatile.Read(ref _providers);

        IXPlatImageFormatProvider? found = null;
        int bestPriority = int.MinValue;

        // Ascending registration order with "at least" rather than "greater" is what makes the most
        // recent registration win at equal priority.
        for (int index = 0; index < providers.Length; index++)
        {
            var provider = providers[index];
            if (provider.Priority >= bestPriority && provider.CanDecode(header))
            {
                found = provider;
                bestPriority = provider.Priority;
            }
        }

        return found;
    }

    private static void Release(IXPlatImageFormatProvider provider)
    {
        lock (Gate)
        {
            if (!Claims.TryGetValue(provider, out int claims))
            {
                // Removed outright while the handle was open; nothing left to release.
                return;
            }

            if (claims > 1)
            {
                Claims[provider] = claims - 1;
                return;
            }

            Claims.Remove(provider);
            RemoveCore(provider);
        }
    }

    /// <summary>Takes a provider out of the list. The caller holds the gate.</summary>
    private static bool RemoveCore(IXPlatImageFormatProvider provider)
    {
        var current = _providers;
        int index = Array.IndexOf(current, provider);
        if (index < 0)
        {
            return false;
        }

        var next = new IXPlatImageFormatProvider[current.Length - 1];
        Array.Copy(current, next, index);
        Array.Copy(current, index + 1, next, index, current.Length - index - 1);
        Volatile.Write(ref _providers, next);
        return true;
    }

    /// <summary>The handle a registration returns: one removal, whenever it is disposed.</summary>
    private sealed class Registration : IDisposable
    {
        private IXPlatImageFormatProvider? _provider;

        internal Registration(IXPlatImageFormatProvider provider) => _provider = provider;

        public void Dispose()
        {
            var provider = Interlocked.Exchange(ref _provider, null);
            if (provider is not null)
            {
                Release(provider);
            }
        }
    }
}

using System.Diagnostics.CodeAnalysis;

namespace Kolvir.Feature.AssetLoading;

/// <summary>
/// Turns the factories an application registered into one question: may anything load this URI as
/// this kind of data, and if so with which loader.
/// </summary>
/// <remarks>
/// <para>
/// The first factory that answers wins. There is no "most specific factory" rule, because there is
/// no ordering the provider could compute that the application cannot express better by registering
/// its factories in the order it wants, and the container preserves registration order
/// (dev-docs/ASSET-LOADING.md section 2.3).
/// </para>
/// <para>
/// It is not a dictionary keyed by scheme: a factory may claim by scheme, by host, by file
/// extension, or by nothing at all - an embedded-resource loader claims a single prefix - and a
/// provider that enumerated the possible keyings would be inventing a taxonomy the factories
/// already know.
/// </para>
/// <para>
/// Calling <c>AddAssetLoading()</c> without registering a factory is a legitimate state rather than
/// a mistake: this answers <see langword="false"/> for every URI, and an application that brings its
/// own factories registers them through <c>AddAssetLoaderFactory</c> (section 2.2).
/// </para>
/// </remarks>
public interface IAssetLoaderProvider
{
    /// <summary>
    /// Asks the registered factories, in registration order, for a loader that reads
    /// <paramref name="uri"/> as a <typeparamref name="TResult"/>.
    /// </summary>
    /// <typeparam name="TResult">The type of data wanted - <c>ImageData</c> for the image
    /// integration.</typeparam>
    /// <param name="uri">Where the data is.</param>
    /// <param name="loader">The first factory's loader, when one answered
    /// <see langword="true"/>; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a factory handled the pair.</returns>
    /// <remarks>
    /// <para>
    /// The loader it hands back is a handle for one request; submitting it to
    /// <see cref="IAssetLoadManager"/> is the caller's next step and the handle's only use. A
    /// second request for the same URI asks this again, and whether that is a second fetch or a
    /// second handle over one is the factory's business (section 4.1).
    /// </para>
    /// <para>
    /// The factories are asked with <c>typeof(TResult)</c> alongside the URI, exactly as
    /// <see cref="IAssetLoaderFactory.TryCreateAssetLoader"/> describes; a factory that claims the
    /// pair and then hands back a loader that does not produce <typeparamref name="TResult"/> is a factory
    /// bug, and it fails loudly here rather than surfacing as an invalid cast at the caller.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A factory claimed the pair and returned a loader
    /// whose result type is not <typeparamref name="TResult"/>, which makes its two answers to
    /// <see cref="IAssetLoaderFactory"/> disagree.</exception>
    bool TryCreateAssetLoader<TResult>(string uri, [NotNullWhen(true)] out IAssetLoader<TResult>? loader);
}

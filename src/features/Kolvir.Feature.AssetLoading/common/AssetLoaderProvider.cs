using System.Diagnostics.CodeAnalysis;

namespace Kolvir.Feature.AssetLoading;

/// <summary>
/// The provider behind <see cref="IAssetLoaderProvider"/>: one ordered list of factories, asked in
/// turn.
/// </summary>
/// <remarks>
/// <para>
/// The list is materialised once, in registration order, because that order is the rule the first
/// match wins by (dev-docs/ASSET-LOADING.md section 2.3). It is not keyed by anything: a factory may
/// claim by scheme, by host, by extension or by a prefix, and a provider that enumerated the possible
/// keyings would be inventing a taxonomy the factories already know.
/// </para>
/// <para>
/// With no factory registered the list is empty and every question answers false, which is the
/// legitimate state <c>AddAssetLoading()</c> on its own produces (section 2.2).
/// </para>
/// </remarks>
internal sealed class AssetLoaderProvider : IAssetLoaderProvider
{
    private readonly IAssetLoaderFactory[] _factories;

    /// <summary>Takes the factories once, in the order the container supplies them.</summary>
    /// <param name="factories">The registered factories.</param>
    public AssetLoaderProvider(IEnumerable<IAssetLoaderFactory> factories) => _factories = [.. factories];

    /// <inheritdoc/>
    public bool TryCreateAssetLoader<TResult>(string uri, [NotNullWhen(true)] out IAssetLoader<TResult>? loader)
    {
        ArgumentNullException.ThrowIfNull(uri);

        foreach (IAssetLoaderFactory factory in _factories)
        {
            // The pair, not the URI alone: a factory may refuse a URI for one kind of data and accept
            // it for another, and the provider applies no rule of its own beyond the ordering (2.1).
            if (!factory.TryCreateAssetLoader(typeof(TResult), uri, out IAssetLoader? created))
            {
                continue;
            }

            loader = created as IAssetLoader<TResult>;

            if (loader is null)
            {
                throw new InvalidOperationException(
                    $"{factory.GetType().Name} said it could make a loader for '{uri}' producing " +
                    $"{typeof(TResult).Name}, and handed back {(created is null ? "null" : created.GetType().Name)}. " +
                    "A factory answers the same question through both members of IAssetLoaderFactory, so the " +
                    "loader it creates has to produce the result type it claimed.");
            }

            return true;
        }

        loader = null;

        return false;
    }
}

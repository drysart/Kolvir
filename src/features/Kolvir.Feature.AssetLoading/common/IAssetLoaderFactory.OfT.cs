using System.Diagnostics.CodeAnalysis;

namespace Kolvir.Feature.AssetLoading;

/// <summary>
/// An <see cref="IAssetLoaderFactory"/> that matches URIs for one result type.
/// </summary>
/// <typeparam name="TResult">The type of data the loaders it makes produce.</typeparam>
/// <remarks>
/// <para>
/// A factory that only cares about URIs for one kind of data implements
/// <see cref="TryCreateAssetLoader"/> and is done: the untyped member - the only one the provider
/// asks - is supplied here, and forwards to it when the caller asked for exactly
/// <typeparamref name="TResult"/>. A factory that spans result types, or whose rule is a type
/// predicate rather than a URI match, implements <see cref="IAssetLoaderFactory"/> directly and uses
/// both of its arguments; nothing about this default stops it.
/// </para>
/// <para>
/// The default's test is <b>exact type equality, not assignability</b>. Assignability would let a
/// factory producing <c>object</c> claim every request in the application, and deciding it needs
/// reflection over the type, which keeps code alive under trimming. <c>==</c> on
/// <see cref="Type"/> is the cheap, trim-friendly answer, and it is a decision rather than an
/// accident of the operator (dev-docs/ASSET-LOADING.md section 2.1).
/// </para>
/// <para>
/// Both members must answer the same question. A factory that overrides the untyped member
/// deliberately, and a disagreement between the two is invisible to the provider, which never asks
/// the typed one.
/// </para>
/// </remarks>
public interface IAssetLoaderFactory<TResult> : IAssetLoaderFactory
{
    /// <summary>
    /// Creates a loader for <paramref name="uri"/> when this factory handles it.
    /// </summary>
    /// <param name="uri">Where the data is.</param>
    /// <param name="loader">The loader, when this factory answered <see langword="true"/>;
    /// otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when this factory handles the URI.</returns>
    /// <remarks>
    /// Written with no cast: the factory that produces <typeparamref name="TResult"/> returns an
    /// <see cref="IAssetLoader{TResult}"/> and the caller that checked the result needs no
    /// null-forgiving operator after it.
    /// </remarks>
    bool TryCreateAssetLoader(string uri, [NotNullWhen(true)] out IAssetLoader<TResult>? loader);

    /// <summary>
    /// Forwards the untyped question to <see cref="TryCreateAssetLoader"/> when the caller asked for
    /// exactly <typeparamref name="TResult"/>.
    /// </summary>
    /// <param name="resultType">The type of data the caller asked for.</param>
    /// <param name="uri">Where the data is.</param>
    /// <param name="loader">The loader, when this interface handles the pair.</param>
    /// <returns><see langword="true"/> when the result type matches exactly and the typed member
    /// handled the URI.</returns>
    /// <remarks>
    /// The comparison is reference equality on <see cref="Type"/>, so <c>typeof(object)</c> does not
    /// claim a request for <c>typeof(string)</c>. Override this member when a factory answers across
    /// result types, and keep the two answers consistent.
    /// </remarks>
    bool IAssetLoaderFactory.TryCreateAssetLoader(
        Type resultType,
        string uri,
        [NotNullWhen(true)] out IAssetLoader? loader)
    {
        if (resultType != typeof(TResult) || !TryCreateAssetLoader(uri, out IAssetLoader<TResult>? typed))
        {
            loader = null;
            return false;
        }

        loader = typed;
        return true;
    }
}

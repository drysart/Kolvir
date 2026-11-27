using System.Diagnostics.CodeAnalysis;

namespace Kolvir.Feature.AssetLoading;

/// <summary>
/// Answers "can you make a loader for this URI that produces this kind of data", and makes one.
/// </summary>
/// <remarks>
/// <para>
/// The question is the pair <c>resultType</c> and <c>uri</c>, not the URI
/// alone, because a factory may refuse a URI for one kind of data and accept it for another -
/// configuration fetched over <c>https</c> might be refused while the same configuration from a local
/// path is fine - and it can only judge that if it is told what the caller asked for alongside where
/// it asked for it (dev-docs/ASSET-LOADING.md section 2.1).
/// </para>
/// <para>
/// A factory is per application, not per request: it is registered once, in
/// <c>AddAssetLoaderFactory</c>, and it is a singleton because it holds the counting state its
/// loaders coordinate through - the fetch refcount, the per-domain limits - which is process-wide by
/// definition (section 2.2). A factory that only matches URIs of one result type implements
/// <see cref="IAssetLoaderFactory{TResult}"/> and gets this member by default; a factory whose rule
/// spans result types, or is a type predicate rather than a URI match, implements this member itself
/// and uses both arguments.
/// </para>
/// <para>
/// The provider only ever asks this member (section 2.3), so a factory that implements both must
/// answer the same question through both - a disagreement is invisible to it.
/// </para>
/// </remarks>
public interface IAssetLoaderFactory
{
    /// <summary>
    /// Creates a loader for <paramref name="uri"/> whose result is a <paramref name="resultType"/>,
    /// when this factory handles that pair.
    /// </summary>
    /// <param name="resultType">The type of data the caller asked for, exactly as it was asked for -
    /// see <see cref="IAssetLoaderFactory{TResult}"/> for why the default compares it exactly.</param>
    /// <param name="uri">Where the data is. Not every loader has one in a conventional form: an
    /// embedded-resource loader claims a single prefix, which is still a string it can match.</param>
    /// <param name="loader">The loader, when this factory answered <see langword="true"/>; otherwise
    /// <see langword="null"/>. It is not created at all on a <see langword="false"/>, so a factory
    /// that declines costs a predicate rather than an allocation.</param>
    /// <returns><see langword="true"/> when this factory handles the pair, <see langword="false"/>
    /// when it does not.</returns>
    /// <remarks>
    /// <para>
    /// The loader this hands back is a handle for one request, not a fetch: it carries the requesting
    /// consumer's priority and token, so the same URI asked for twice yields two handles over
    /// whatever the factory shares underneath (section 4.1).
    /// </para>
    /// <para>
    /// The provider asks its factories in registration order and the first match wins; there is no
    /// "most specific factory" rule, because the application can express the ordering better by
    /// registering in the order it wants (section 2.3).
    /// </para>
    /// </remarks>
    bool TryCreateAssetLoader(Type resultType, string uri, [NotNullWhen(true)] out IAssetLoader? loader);
}

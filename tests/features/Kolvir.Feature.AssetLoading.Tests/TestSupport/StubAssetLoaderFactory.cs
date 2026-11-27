using System.Diagnostics.CodeAnalysis;

namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>A factory that claims a URI prefix, or every URI, and counts what it was asked.</summary>
/// <typeparam name="TResult">What its loaders produce.</typeparam>
internal sealed class StubAssetLoaderFactory<TResult> : IAssetLoaderFactory<TResult>
{
    private readonly string? _claims;

    /// <summary>Creates a factory that claims one prefix, or every URI when none is given.</summary>
    /// <param name="claims">The only prefix it answers for, or null to answer for every URI.</param>
    public StubAssetLoaderFactory(string? claims = null) => _claims = claims;

    /// <summary>How many loaders this factory has made.</summary>
    public int CreateCount { get; private set; }

    /// <summary>The URIs the typed member was asked about, in order.</summary>
    public List<string> Asked { get; } = [];

    /// <inheritdoc/>
    public bool TryCreateAssetLoader(string uri, [NotNullWhen(true)] out IAssetLoader<TResult>? loader)
    {
        Asked.Add(uri);

        if (_claims is not null && !uri.StartsWith(_claims, StringComparison.Ordinal))
        {
            loader = null;
            return false;
        }

        CreateCount++;
        loader = new FakeAssetLoader<TResult>();

        return true;
    }
}

/// <summary>
/// A factory that answers the untyped member itself, because its rule is a type predicate rather than
/// a URI match - the case <see cref="IAssetLoaderFactory"/>'s own argument exists for.
/// </summary>
internal sealed class TypedPredicateFactory : IAssetLoaderFactory
{
    /// <inheritdoc/>
    public bool TryCreateAssetLoader(Type resultType, string uri, [NotNullWhen(true)] out IAssetLoader? loader)
    {
        if (resultType == typeof(int) && uri == "number")
        {
            loader = new FakeAssetLoader<int>();
            return true;
        }

        loader = null;
        return false;
    }
}

/// <summary>
/// A factory that claims a result type and then hands back a loader that does not produce it, which
/// is the disagreement between the two members the provider has to refuse rather than cast past.
/// </summary>
internal sealed class MismatchedFactory : IAssetLoaderFactory
{
    /// <inheritdoc/>
    public bool TryCreateAssetLoader(Type resultType, string uri, [NotNullWhen(true)] out IAssetLoader? loader)
    {
        loader = new FakeAssetLoader<string>();

        return true;
    }
}

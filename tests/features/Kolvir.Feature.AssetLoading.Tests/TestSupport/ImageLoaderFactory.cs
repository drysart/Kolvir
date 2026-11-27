using System.Diagnostics.CodeAnalysis;

namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>
/// A factory that claims every URI and hands out loaders the test completes by hand, keeping each one
/// so a test can inspect the request that was made for it.
/// </summary>
/// <remarks>
/// The loaders are <see cref="FakeAssetLoader{TResult}"/>s completed with a
/// <see cref="TestImage"/>, so a test can tell a load that was cancelled from one that was released.
/// </remarks>
internal sealed class ImageLoaderFactory : IAssetLoaderFactory<ImageData>
{
    /// <summary>Every loader the provider has asked for, in order.</summary>
    public List<FakeAssetLoader<ImageData>> Loaders { get; } = [];

    /// <summary>The URIs the provider asked about, in order.</summary>
    public List<string> Asked { get; } = [];

    /// <summary>
    /// Runs for each loader the factory makes, so a test can make it complete on start, fail, or hold
    /// its gate shut before the control ever sees it.
    /// </summary>
    public Action<FakeAssetLoader<ImageData>>? Configure { get; set; }

    /// <inheritdoc/>
    public bool TryCreateAssetLoader(string uri, [NotNullWhen(true)] out IAssetLoader<ImageData>? loader)
    {
        Asked.Add(uri);

        var fake = new FakeAssetLoader<ImageData>();
        Configure?.Invoke(fake);
        Loaders.Add(fake);
        loader = fake;

        return true;
    }

    /// <summary>The loader for the last request, or null when none was made.</summary>
    public FakeAssetLoader<ImageData>? Last => Loaders.Count == 0 ? null : Loaders[^1];
}

/// <summary>
/// A factory that claims nothing, which is an application with the infrastructure registered and no
/// loader family at all.
/// </summary>
internal sealed class RefusingImageLoaderFactory : IAssetLoaderFactory<ImageData>
{
    /// <inheritdoc/>
    public bool TryCreateAssetLoader(string uri, [NotNullWhen(true)] out IAssetLoader<ImageData>? loader)
    {
        loader = null;

        return false;
    }
}

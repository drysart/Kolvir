namespace Kolvir.Feature.AssetLoading;

/// <summary>
/// An <see cref="IAssetLoader"/> whose result is known to be a <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TResult">What the load produces - <c>ImageData</c> for the image integration.</typeparam>
/// <remarks>
/// <para>
/// This is the form an application writes and a caller awaits: the redeclared member means
/// <c>await manager.LoadAsync(loader, onStarted, token)</c> is a <c>Task&lt;ImageData&gt;</c>, so
/// nobody awaits a <c>Task&lt;object?&gt;</c> by accident and then casts (dev-docs/ASSET-LOADING.md
/// section 3.14).
/// </para>
/// <para>
/// Both members must be implemented, and both must be the same load: a class implementing this
/// interface writes <see cref="LoadAsync(CancellationToken)"/> as the typed method the interface
/// asks for, and satisfies <see cref="IAssetLoader.LoadAsync"/> either by implementing it
/// explicitly or by having that method's task behind the same work. The manager only ever calls the
/// untyped member, so a typed implementation that returned a different task would be observed by
/// nobody.
/// </para>
/// <para>
/// The factory that makes these is <see cref="IAssetLoaderFactory{TResult}"/>, and it is the
/// factory - not the loader - that owns the byte-level sharing described in section 4.1.
/// </para>
/// </remarks>
public interface IAssetLoader<TResult> : IAssetLoader
{
    /// <summary>Starts the load, completing with the result the caller asked for.</summary>
    /// <param name="cancellationToken">The caller's token; see
    /// <see cref="IAssetLoader.LoadAsync"/> for what the manager does and does not rely on.</param>
    /// <returns>A task that completes with the loaded value, or faults with the reason it failed.</returns>
    new Task<TResult> LoadAsync(CancellationToken cancellationToken);
}

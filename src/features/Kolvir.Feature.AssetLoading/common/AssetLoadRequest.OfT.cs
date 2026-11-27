namespace Kolvir.Feature.AssetLoading;

/// <summary>
/// One request, typed by what it produces, so the caller gets a <see cref="Task{TResult}"/> and not
/// an <see cref="object"/> it would have to cast.
/// </summary>
/// <typeparam name="TResult">What the load produces.</typeparam>
/// <remarks>
/// <para>
/// The completion source is deliberately created without
/// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>: a caller that has attached a
/// continuation is answered inside the manager's own step, which is the re-entrancy the walk has to
/// survive rather than something to push onto the thread pool (dev-docs/ASSET-LOADING.md
/// section 3.8). In an application that step is already on the UI thread with the UI context current,
/// so the caller's continuation runs inline there rather than being posted back to it (section 3.12).
/// </para>
/// <para>
/// A cancellation is reported with the caller's token, so <c>TaskCanceledException.CancellationToken</c>
/// is the token that asked to stop rather than a fresh one.
/// </para>
/// </remarks>
internal sealed class AssetLoadRequest<TResult> : AssetLoadRequest
{
    private readonly TaskCompletionSource<TResult> _completion = new();

    public AssetLoadRequest(IAssetLoader<TResult> loader, Action? onStarted, long sequence, CancellationToken cancellationToken)
        : base(loader, onStarted, sequence, cancellationToken)
    {
    }

    /// <summary>The caller's task, which <see cref="IAssetLoadManager.LoadAsync{TResult}"/> returns.</summary>
    public Task<TResult> Completion => _completion.Task;

    /// <inheritdoc/>
    public override bool Delivered => _completion.Task.Status == TaskStatus.RanToCompletion;

    /// <inheritdoc/>
    public override bool HandOver(object? result)
    {
        TResult value;

        try
        {
            value = (TResult)result!;
        }
        catch (NullReferenceException nullResult)
        {
            // Unboxing null: a typed loader over a value type that produced nothing. The runtime's
            // message names neither the loader nor the type it was expected to produce, and both are
            // what an author has to look at.
            throw new InvalidOperationException(
                $"The loader {Loader.GetType()} produced no result for a {typeof(TResult)} request. A " +
                "typed loader over a value type has to produce one, so an untyped member that completes " +
                "with null is a factory bug rather than a load that produced nothing.",
                nullResult);
        }

        return _completion.TrySetResult(value);
    }

    /// <inheritdoc/>
    public override void Fail(Exception error) => _completion.TrySetException(error);

    /// <inheritdoc/>
    public override void Cancel() => _completion.TrySetCanceled(CancellationToken);
}

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
/// section 3.8). In an application the ambient <c>UiSynchronizationContext</c> then puts that
/// continuation back on the UI thread, which is where a caller assigns what it loaded (section 3.12).
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
    public override void HandOver(object? result) => _completion.TrySetResult((TResult)result!);

    /// <inheritdoc/>
    public override void Fail(Exception error) => _completion.TrySetException(error);

    /// <inheritdoc/>
    public override void Cancel() => _completion.TrySetCanceled(CancellationToken);
}

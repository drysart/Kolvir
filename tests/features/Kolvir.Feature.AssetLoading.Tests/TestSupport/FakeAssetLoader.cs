namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>
/// A loader whose every input and every output belongs to the test: availability is a field it sets,
/// the task is one it completes, and the handler pairs are ordinary lists it can count.
/// </summary>
/// <typeparam name="TResult">What the load produces.</typeparam>
/// <remarks>
/// There is no timer and nothing to wait for. A test that wants a load to start sets
/// <see cref="CanLoadNow"/> and raises the notification, a test that wants one to finish calls
/// <see cref="Complete"/>, and a test that wants one to finish on the spot sets
/// <see cref="CompletesOnStart"/> - so every assertion is about what the manager did rather than
/// about how long the test slept (dev-docs/ASSET-LOADING.md section 7.1).
/// </remarks>
internal sealed class FakeAssetLoader<TResult> : IAssetLoader<TResult>
{
    private readonly TaskCompletionSource<object?> _completion = new();
    private readonly List<Action<object>> _priorityHandlers = [];
    private readonly List<Action<object>> _canLoadNowHandlers = [];

    /// <summary>Creates a loader at a priority and with a gate, both the test's to change.</summary>
    /// <param name="priority">Where the request queues.</param>
    /// <param name="canLoadNow">Whether it may start straight away.</param>
    public FakeAssetLoader(int priority = AssetLoadPriority.Background, bool canLoadNow = true)
    {
        Priority = priority;
        CanLoadNow = canLoadNow;
    }

    /// <inheritdoc/>
    public int Priority { get; set; }

    /// <inheritdoc/>
    public bool CanLoadNow { get; set; }

    /// <summary>
    /// True when <see cref="LoadAsync"/> completes the load before it returns, which is the cache-hit
    /// case the walk has to survive completing synchronously.
    /// </summary>
    public bool CompletesOnStart { get; set; }

    /// <summary>What <see cref="CompletesOnStart"/> completes with.</summary>
    public TResult? OnStartResult { get; set; }

    /// <summary>An exception <see cref="LoadAsync"/> throws instead of returning a task.</summary>
    public Exception? StartError { get; set; }

    /// <summary>
    /// True when the load finishes from a callback on the caller's token, which is what a loader does
    /// when it treats the token as "stop, and hand back whatever you have".
    /// </summary>
    /// <remarks>
    /// The callback is registered when the load starts, which is after the manager registered its own
    /// - and <c>CancellationToken.Register</c> invokes callbacks last-registered-first, so this one
    /// runs <em>before</em> the manager's and the task is completed from inside it. That is the
    /// ordering the manager's answer must not depend on (dev-docs/ASSET-LOADING.md section 3.9).
    /// </remarks>
    public bool CompletesWhenCancelled { get; set; }

    /// <summary>What <see cref="CompletesWhenCancelled"/> completes with.</summary>
    public TResult? OnCancelResult { get; set; }

    /// <summary>What <see cref="CompletesWhenCancelled"/> fails with, when it fails instead.</summary>
    public Exception? OnCancelError { get; set; }

    /// <summary>How many times the manager has asked this loader to load.</summary>
    public int LoadCount { get; private set; }

    /// <summary>The token the last load was handed.</summary>
    public CancellationToken Token { get; private set; }

    /// <summary>How many priority handlers are subscribed; the manager's is the only one.</summary>
    public int PriorityHandlerCount => _priorityHandlers.Count;

    /// <summary>How many availability handlers are subscribed; the manager's is the only one.</summary>
    public int CanLoadNowHandlerCount => _canLoadNowHandlers.Count;

    /// <inheritdoc/>
    public IDisposable AddPriorityChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _priorityHandlers.Add(handler);

        return new HandlerToken<Action<object>>(RemovePriorityChangedHandler, handler);
    }

    /// <inheritdoc/>
    public void RemovePriorityChangedHandler(Action<object> handler) => _priorityHandlers.Remove(handler);

    /// <inheritdoc/>
    public IDisposable AddCanLoadNowChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _canLoadNowHandlers.Add(handler);

        return new HandlerToken<Action<object>>(RemoveCanLoadNowChangedHandler, handler);
    }

    /// <inheritdoc/>
    public void RemoveCanLoadNowChangedHandler(Action<object> handler) => _canLoadNowHandlers.Remove(handler);

    /// <summary>Changes the priority and tells the manager, which is what a scroll does.</summary>
    /// <param name="priority">The new priority.</param>
    public void SetPriority(int priority)
    {
        Priority = priority;
        Raise(_priorityHandlers);
    }

    /// <summary>Opens or closes the gate and tells the manager, which is what a factory does.</summary>
    /// <param name="canLoadNow">The new answer.</param>
    public void SetCanLoadNow(bool canLoadNow)
    {
        CanLoadNow = canLoadNow;
        Raise(_canLoadNowHandlers);
    }

    /// <summary>Finishes the load with a value.</summary>
    /// <param name="result">What the load produced.</param>
    public void Complete(TResult result) => _completion.SetResult(result);

    /// <summary>Finishes the load with a failure.</summary>
    /// <param name="error">What the load threw.</param>
    public void Fail(Exception error) => _completion.SetException(error);

    /// <summary>
    /// Finishes the load with a value the typed member cannot produce, which is what a loader whose
    /// two members disagree hands back. Only a broken loader does this.
    /// </summary>
    /// <param name="result">Some value of another type.</param>
    public void CompleteWithTheWrongType(object? result) => _completion.SetResult(result);

    /// <inheritdoc/>
    public async Task<TResult> LoadAsync(CancellationToken cancellationToken) =>
        (TResult)(await Begin(cancellationToken))!;

    /// <inheritdoc/>
    Task<object?> IAssetLoader.LoadAsync(CancellationToken cancellationToken) => Begin(cancellationToken);

    private Task<object?> Begin(CancellationToken cancellationToken)
    {
        if (StartError is not null)
        {
            throw StartError;
        }

        LoadCount++;
        Token = cancellationToken;

        if (CompletesWhenCancelled)
        {
            cancellationToken.Register(CompleteWhenCancelled);
        }

        if (CompletesOnStart)
        {
            _completion.SetResult(OnStartResult);
        }

        return _completion.Task;
    }

    /// <summary>Finishes the load from inside the caller's token callback.</summary>
    private void CompleteWhenCancelled()
    {
        if (OnCancelError is not null)
        {
            _completion.SetException(OnCancelError);
            return;
        }

        _completion.SetResult(OnCancelResult);
    }

    /// <summary>Raises a notification over a copy, so a handler may subscribe or remove while it
    /// runs.</summary>
    /// <param name="handlers">The handler list to raise.</param>
    private void Raise(List<Action<object>> handlers)
    {
        foreach (Action<object> handler in handlers.ToArray())
        {
            handler(this);
        }
    }
}

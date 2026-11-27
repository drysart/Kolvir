namespace Kolvir.Feature.AssetLoading.Http;

/// <summary>
/// One consumer's handle for one HTTP asset request.
/// </summary>
/// <remarks>
/// <para>
/// It carries that consumer's priority and that consumer's cancellation token, and it holds no bytes
/// and no image. What it shares - the request and the encoded response - lives in the factory, one
/// fetch per URI, refcounted by the handles over it
/// (dev-docs/ASSET-LOADING.md sections 3.1 and 4.1).
/// </para>
/// <para>
/// The started priority is <see cref="AssetLoadPriority.Background"/>. Nothing has classified this
/// load as visible - that is the control integration's job, which moves it when the image is on
/// screen (section 3.15) - and treating an unclassified load as visible would let it compete with
/// what the user is looking at, which is the one mistake the priority queue exists to prevent
/// (section 2.5).
/// </para>
/// <para>
/// <b>Everything here is UI-thread state</b>, because the manager's walk reads it: a handle is
/// created, prioritised and cancelled on the thread that owns the controls it was made for. The
/// notification raised from the fetch's thread is the exception, and it is safe because the manager
/// moves it onto the UI thread before touching its queue (section 3.12).
/// </para>
/// </remarks>
internal sealed class HttpAssetLoader : IAssetLoader<ImageData>
{
    private readonly HttpAssetLoaderFactory _factory;
    private readonly string _uri;
    private readonly List<Action<object>> _priorityHandlers = [];
    private readonly List<Action<object>> _canLoadNowHandlers = [];
    private readonly object _handlerSync = new();

    private int _priority = AssetLoadPriority.Background;

    /// <summary>Makes a handle. The factory is the only thing that should call this.</summary>
    /// <param name="factory">The factory that owns the request this handle will share.</param>
    /// <param name="uri">The canonical absolute URI to load.</param>
    /// <param name="host">The host the per-host limit is counted against.</param>
    public HttpAssetLoader(HttpAssetLoaderFactory factory, string uri, string host)
    {
        _factory = factory;
        _uri = uri;
        Host = host;
    }

    /// <summary>The host this handle's request is counted against, exposed for the factory's waiters.</summary>
    public string Host { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// Setting it raises the priority-changed notification, because the manager's queue order is
    /// stale the moment the value changes; a caller that sets it through the property does not also
    /// have to know to raise the handler. Re-prioritising never preempts a running fetch
    /// (dev-docs/ASSET-LOADING.md section 3.5).
    /// </remarks>
    public int Priority
    {
        get => _priority;
        set
        {
            if (_priority == value)
            {
                return;
            }

            _priority = value;
            Raise(_priorityHandlers);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Three answers, in this order: a fetch for this URI is already in flight, so this handle would
    /// share it and consume no new slot; or the request would fit the total and its host's limit; or
    /// it would not. Asking changes none of those - the count moves when
    /// <see cref="LoadAsync"/> attaches the handle, which is what keeps the manager's walk from
    /// overshooting (section 4.2).
    /// </remarks>
    public bool CanLoadNow => _factory.CanLoadNow(_uri, Host);

    /// <inheritdoc/>
    public IDisposable AddPriorityChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_handlerSync)
        {
            _priorityHandlers.Add(handler);
        }

        return new HandlerToken<Action<object>>(RemovePriorityChangedHandler, handler);
    }

    /// <inheritdoc/>
    public void RemovePriorityChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_handlerSync)
        {
            _priorityHandlers.Remove(handler);
        }
    }

    /// <inheritdoc/>
    public IDisposable AddCanLoadNowChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_handlerSync)
        {
            _canLoadNowHandlers.Add(handler);
        }

        return new HandlerToken<Action<object>>(RemoveCanLoadNowChangedHandler, handler);
    }

    /// <inheritdoc/>
    public void RemoveCanLoadNowChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_handlerSync)
        {
            _canLoadNowHandlers.Remove(handler);
        }
    }

    /// <inheritdoc/>
    public Task<ImageData> LoadAsync(CancellationToken cancellationToken) =>
        _factory.LoadAsync(this, _uri, Host, cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// The same load with the result widened to <see cref="object"/>. <see cref="ImageData"/> is a
    /// class, so the widening is a reference conversion and there is no boxed copy - the case
    /// section 3.14 says is free. The wrapper is a task because <see cref="Task{TResult}"/> is not
    /// covariant; a struct result would need a real box, and no loader of one is planned.
    /// </remarks>
    async Task<object?> IAssetLoader.LoadAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken);

    /// <summary>Tells this handle's subscribers to ask <see cref="CanLoadNow"/> again.</summary>
    /// <remarks>
    /// Called by the factory when a slot freed for this handle's host, or when a limit was raised -
    /// from the thread that freed the slot or made the change, which may be a pool thread. A
    /// notification that reaches nobody is the harmless case: a handle whose request was cancelled
    /// while queued has no subscriber left.
    /// </remarks>
    internal void RaiseCanLoadNowChanged() => Raise(_canLoadNowHandlers);

    /// <summary>Raises one handler list over a copy, so a handler may subscribe or remove while it runs.</summary>
    /// <param name="handlers">The list to raise.</param>
    private void Raise(List<Action<object>> handlers)
    {
        Action<object>[] snapshot;

        lock (_handlerSync)
        {
            snapshot = [.. handlers];
        }

        foreach (Action<object> handler in snapshot)
        {
            handler(this);
        }
    }
}

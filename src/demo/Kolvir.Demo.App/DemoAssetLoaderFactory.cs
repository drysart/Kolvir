using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Kolvir.Feature.AssetLoading;

namespace Kolvir.Demo;

/// <summary>
/// The demo's loader factory, and the gate that makes its queue watchable.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the demo has its own factory.</b> Nothing in the repository ships a loader: the feature is
/// the manager and the control integration, and every loader family is an application's opt-in
/// (dev-docs/ASSET-LOADING.md section 2.2). The manager's own tests use a fake loader whose gate is
/// a field a test sets, which proves the queue obeys the priority it is handed and nothing about a
/// factory that really counts. This factory is that second half: it counts its own fetches, holds a
/// small limit per host, and raises <see cref="IAssetLoader.CanLoadNowChanged"/> from the thread that
/// finished one, exactly as section 4.2 describes.
/// </para>
/// <para>
/// <b>The gate is demo-local on purpose.</b> Section 4.2 says a reusable gate helper is open question
/// 9.1 rather than a decision, and section 9 says its shape should be written against a real factory
/// rather than guessed. So this is a gate written against this demo and kept in the demo: it has the
/// shape the rule needs and none of the general-purpose surface a shipped helper would have to
/// promise. It is not a candidate for the feature package.
/// </para>
/// <para>
/// <b>It reserves nothing.</b> <see cref="DemoAssetLoader.CanLoadNow"/> reports whether there is room
/// <em>now</em>, and asking does not consume a slot. The count moves when
/// <see cref="DemoAssetLoader.LoadAsync"/> is entered, which is synchronous with the manager's
/// decision to start that load, so a walk cannot overshoot by starting everything while the gate
/// still says yes (section 4.2). The notification means "ask again" and is raised only on the handles
/// of the host whose slot was freed, and only on the ones that have not started, which is the
/// manager's queued set.
/// </para>
/// <para>
/// <b>No network.</b> A URI names a row, not a file somewhere. The bytes are the tiles
/// <see cref="DemoTiles"/> draws from the sample's own SkiaSharp surface, and the fetch's only
/// artificial part is a short wait so that a queue with a limit of three is something a person can
/// see rather than something that has already finished by the first frame.
/// </para>
/// </remarks>
internal sealed class DemoAssetLoaderFactory : IAssetLoaderFactory<ImageData>
{
    /// <summary>The scheme the demo claims. Every other scheme is refused.</summary>
    internal const string Scheme = "demo";

    /// <summary>How many fetches one host may have in flight at once.</summary>
    internal const int MaxConcurrentFetchesPerHost = 3;

    /// <summary>
    /// How long a fetch takes. It is the demo's transport, not the feature: a local tile would
    /// arrive in one turn and the list would be finished before anything could be watched.
    /// </summary>
    private static readonly TimeSpan FetchDuration = TimeSpan.FromMilliseconds(220);

    private readonly object _lock = new();
    private readonly Dictionary<string, HostGate> _hosts = new(StringComparer.Ordinal);
    private readonly List<int> _startedRows = [];

    private int _fetches;

    /// <summary>How many fetches are in flight across every host right now.</summary>
    internal int InFlight
    {
        get
        {
            lock (_lock)
            {
                int total = 0;

                foreach (HostGate gate in _hosts.Values)
                {
                    total += gate.Active;
                }

                return total;
            }
        }
    }

    /// <summary>How many fetches this factory has started since the application began.</summary>
    internal int FetchCount
    {
        get
        {
            lock (_lock)
            {
                return _fetches;
            }
        }
    }

    /// <summary>
    /// The rows whose fetches started, in the order the manager started them.
    /// </summary>
    /// <remarks>
    /// This is the page's evidence that the priority it sees really is the order the queue ran in.
    /// It records the row at the moment <see cref="IAssetLoader.LoadAsync"/> is entered, which is the
    /// manager's own decision point, not when a result arrives.
    /// </remarks>
    internal int[] StartedRows
    {
        get
        {
            lock (_lock)
            {
                return [.. _startedRows];
            }
        }
    }

    /// <inheritdoc/>
    public bool TryCreateAssetLoader(string uri, [NotNullWhen(true)] out IAssetLoader<ImageData>? loader)
    {
        if (!TryParse(uri, out string host, out int row))
        {
            loader = null;

            return false;
        }

        var created = new DemoAssetLoader(this, host, row);

        lock (_lock)
        {
            GateFor(host).Handles.Add(new WeakReference<DemoAssetLoader>(created));
        }

        loader = created;

        return true;
    }

    /// <summary>Whether <paramref name="host"/> has room for another fetch right now.</summary>
    /// <param name="host">The host whose gate to read.</param>
    /// <returns>True while the host's in-flight count is below the limit. Nothing is reserved.</returns>
    internal bool HasRoom(string host)
    {
        lock (_lock)
        {
            return GateFor(host).Active < MaxConcurrentFetchesPerHost;
        }
    }

    /// <summary>
    /// Starts one fetch: counts it, records its row, and hands back the bytes after the demo's
    /// transport delay.
    /// </summary>
    /// <param name="handle">The loader that is starting.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The encoded tile.</returns>
    /// <remarks>
    /// The count and the start log are written before the first await, so they are in place when
    /// <see cref="LoadAsync"/> returns its task - the manager's walk reads the next handle's gate
    /// immediately after starting one. The release is in a <c>finally</c>, so a cancelled fetch frees
    /// its slot too, and it runs on the thread that finished the fetch, which is where section 4.2
    /// says the notification is raised from.
    /// </remarks>
    internal async Task<byte[]> FetchAsync(DemoAssetLoader handle, CancellationToken cancellationToken)
    {
        BeginFetch(handle);

        try
        {
            await Task.Delay(FetchDuration, cancellationToken).ConfigureAwait(false);

            return DemoTiles.Png(handle.Row);
        }
        finally
        {
            EndFetch(handle);
        }
    }

    private void BeginFetch(DemoAssetLoader handle)
    {
        lock (_lock)
        {
            GateFor(handle.Host).Active++;

            // Read by EndFetch to decide who is still waiting. It is set under the same lock the
            // handle list is read under, because the two can be touched from different threads.
            handle.Started = true;

            _fetches++;
            _startedRows.Add(handle.Row);
        }
    }

    private void EndFetch(DemoAssetLoader handle)
    {
        List<DemoAssetLoader> waiting = [];

        lock (_lock)
        {
            HostGate gate = GateFor(handle.Host);
            gate.Active--;

            List<WeakReference<DemoAssetLoader>> handles = gate.Handles;

            for (int index = handles.Count - 1; index >= 0; index--)
            {
                if (!handles[index].TryGetTarget(out DemoAssetLoader? candidate))
                {
                    handles.RemoveAt(index);
                    continue;
                }

                if (!candidate.Started)
                {
                    waiting.Add(candidate);
                }
            }
        }

        // Outside the lock, because a handler posts to the manager and the manager may read
        // CanLoadNow - which takes this same lock - from another thread before this one returns.
        foreach (DemoAssetLoader candidate in waiting)
        {
            candidate.NotifyCanLoadNowChanged();
        }
    }

    private HostGate GateFor(string host)
    {
        if (!_hosts.TryGetValue(host, out HostGate? gate))
        {
            gate = new HostGate();
            _hosts.Add(host, gate);
        }

        return gate;
    }

    private static bool TryParse(string uri, out string host, out int row)
    {
        host = string.Empty;
        row = 0;

        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) ||
            !string.Equals(parsed.Scheme, Scheme, StringComparison.Ordinal) ||
            parsed.Host.Length == 0)
        {
            return false;
        }

        if (!int.TryParse(
                parsed.AbsolutePath.Trim('/'),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out row))
        {
            return false;
        }

        host = parsed.Host;

        return true;
    }

    /// <summary>The counting state one host's loaders coordinate through.</summary>
    private sealed class HostGate
    {
        /// <summary>Fetches in flight. Written only under the factory's lock.</summary>
        public int Active;

        /// <summary>
        /// Every handle made for this host, weakly: a queued handle is kept alive by the manager,
        /// and one the manager has already released must not be kept alive by the factory.
        /// </summary>
        public List<WeakReference<DemoAssetLoader>> Handles { get; } = [];
    }
}

/// <summary>
/// One request's handle, as <see cref="DemoAssetLoaderFactory"/> makes it: this consumer's priority,
/// this consumer's availability answer, and one fetch underneath.
/// </summary>
/// <remarks>
/// Everything the manager touches is here. <see cref="Priority"/> is refused by the manager as stale
/// only if the setter raises, which is why it raises; <see cref="CanLoadNow"/> asks the factory rather
/// than caching an answer; and the handler lists are locked because a factory may raise the
/// availability notification from a fetch thread while the manager subscribes or unsubscribes on the
/// UI thread (section 3.12). An instance is one request, so the demo never hands the same handle out
/// twice.
/// </remarks>
internal sealed class DemoAssetLoader : IAssetLoader<ImageData>
{
    private readonly DemoAssetLoaderFactory _factory;
    private readonly object _handlers = new();
    private readonly List<Action<object>> _priorityHandlers = [];
    private readonly List<Action<object>> _canLoadNowHandlers = [];

    private int _priority;

    /// <summary>Creates the handle the factory hands to the provider.</summary>
    /// <param name="factory">The factory that owns the gate.</param>
    /// <param name="host">The gate's key, taken from the URI.</param>
    /// <param name="row">The row the URI names, which is also the tile's number.</param>
    public DemoAssetLoader(DemoAssetLoaderFactory factory, string host, int row)
    {
        _factory = factory;
        Host = host;
        Row = row;
    }

    /// <summary>The row this request is for, which is also the number on its tile.</summary>
    internal int Row { get; }

    /// <summary>The gate this handle is counted against.</summary>
    internal string Host { get; }

    /// <summary>True once <see cref="LoadAsync"/> has been entered; written under the gate's lock.</summary>
    internal bool Started { get; set; }

    /// <inheritdoc/>
    /// <remarks>Setting it raises <see cref="AddPriorityChangedHandler"/>, without which a scroll
    /// would change the value the snapshot reports and not the order the queue runs in.</remarks>
    public int Priority
    {
        get => _priority;
        set
        {
            if (value == _priority)
            {
                return;
            }

            _priority = value;
            Raise(_priorityHandlers);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The gate's answer, not this handle's history: a queued handle asks "is there room now", and a
    /// running one is not asked at all.
    /// </remarks>
    public bool CanLoadNow => _factory.HasRoom(Host);

    /// <inheritdoc/>
    public IDisposable AddPriorityChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_handlers)
        {
            _priorityHandlers.Add(handler);
        }

        return new HandlerToken<Action<object>>(RemovePriorityChangedHandler, handler);
    }

    /// <inheritdoc/>
    public void RemovePriorityChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_handlers)
        {
            _priorityHandlers.Remove(handler);
        }
    }

    /// <inheritdoc/>
    public IDisposable AddCanLoadNowChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_handlers)
        {
            _canLoadNowHandlers.Add(handler);
        }

        return new HandlerToken<Action<object>>(RemoveCanLoadNowChangedHandler, handler);
    }

    /// <inheritdoc/>
    public void RemoveCanLoadNowChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_handlers)
        {
            _canLoadNowHandlers.Remove(handler);
        }
    }

    /// <summary>Raises the availability notification; called by the factory from a fetch thread.</summary>
    internal void NotifyCanLoadNowChanged() => Raise(_canLoadNowHandlers);

    /// <inheritdoc/>
    /// <remarks>
    /// The fetch runs off the thread and the decode runs on the one that called this, because
    /// <c>FetchAsync</c> awaits its delay with <c>ConfigureAwait(false)</c> and this await does not
    /// (section 3.12): the bytes may be read anywhere, but the <see cref="ImageData"/> is composed on
    /// the UI thread, where the manager delivers the result.
    /// </remarks>
    public Task<ImageData> LoadAsync(CancellationToken cancellationToken) =>
        LoadCoreAsync(cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// The manager holds an <see cref="IAssetLoader"/>, so this is the member it calls; the typed
    /// form is what a caller's <c>await</c> gets (section 3.14). Both run the same work, and the
    /// image is a class, so nothing is boxed.
    /// </remarks>
    async Task<object?> IAssetLoader.LoadAsync(CancellationToken cancellationToken) =>
        await LoadCoreAsync(cancellationToken);

    private async Task<ImageData> LoadCoreAsync(CancellationToken cancellationToken)
    {
        byte[] bytes = await _factory.FetchAsync(this, cancellationToken);

        return DemoPage.Images.LoadFromBytes(bytes);
    }

    private void Raise(List<Action<object>> handlers)
    {
        Action<object>[] copy;

        lock (_handlers)
        {
            if (handlers.Count == 0)
            {
                return;
            }

            copy = [.. handlers];
        }

        foreach (Action<object> handler in copy)
        {
            handler(this);
        }
    }
}

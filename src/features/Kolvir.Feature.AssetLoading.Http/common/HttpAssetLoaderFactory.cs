using System.Diagnostics.CodeAnalysis;

namespace Kolvir.Feature.AssetLoading.Http;

/// <summary>
/// The factory behind <see cref="IAssetLoaderFactory{TResult}"/> for <c>http</c> and <c>https</c>
/// URIs: it decides what it claims, shares one fetch per URI, enforces both limits, and composes the
/// image each handle asked for.
/// </summary>
/// <remarks>
/// <para>
/// <b>One request per URI at a time.</b> The in-flight map is keyed by the canonical absolute URI,
/// and every handle that asks for that URI while the request is running attaches to the same
/// <see cref="HttpAssetFetch"/> instead of starting a second. The map holds <see cref="byte"/> arrays,
/// never <see cref="ImageData"/>: each handle decodes its own image from the shared bytes, which is
/// the invariant that lets the manager dispose a result nobody received without disturbing anybody
/// else (dev-docs/ASSET-LOADING.md section 4.1).
/// </para>
/// <para>
/// <b>The map is not a cache.</b> The entry is removed the moment the request has an outcome - in
/// <see cref="End"/> - and not when the last handle releases. A completed fetch is never served
/// again, so a second load of the same URI is a second request; the bytes stay alive only through the
/// handles that are still decoding them, and are dropped when the last one has. A cache would be a
/// store of completed bytes consulted *before* a fetch starts, with this same in-flight map beside it
/// for the "somebody else is already fetching it" case; it would have to copy what it keeps, because
/// the loader's array is released rather than lent. That is the seam this design leaves, and nothing
/// here has to be rewritten to place it: <see cref="LoadAsync"/> is where the two lookups meet.
/// </para>
/// <para>
/// <b>Capacity counts fetches, not handles.</b> A second handle over a running fetch consumes no slot
/// and is allowed to start even when the host is at its limit, because starting it asks the network
/// for nothing. That is the number section 3.1 says a limit has to be stated in.
/// </para>
/// <para>
/// <b>Notifications go to the handles that could have been waiting for the thing that changed.</b>
/// A fetch ending frees both a total slot and its host's; the waiters are told when the total was at
/// its cap (any host's request could now fit) and otherwise only that host's (no other host's gate
/// moved). A limit being raised is a cap that applies to every host, so every waiter is told to ask
/// again. Nothing here decides that a request may start - the manager walks, and each handle's
/// <see cref="CanLoadNow"/> answers for itself.
/// </para>
/// </remarks>
internal sealed class HttpAssetLoaderFactory : IAssetLoaderFactory<ImageData>
{
    private readonly IImageDataFactory _images;
    private readonly HttpAssetLoaderOptions _options;
    private readonly object _sync = new();
    private readonly Dictionary<string, HttpAssetFetch> _fetches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _perHost = new(StringComparer.Ordinal);
    private readonly List<WeakReference<HttpAssetLoader>> _waiting = [];

    private HttpClient? _client;
    private bool _ownsClient;
    private int _totalInFlight;

    /// <summary>Builds the factory the container resolves: the image factory, the options and nothing else.</summary>
    /// <param name="images">The application's image decoder, composed from its format providers.</param>
    /// <param name="options">The settings, which this factory reads live.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public HttpAssetLoaderFactory(IImageDataFactory images, HttpAssetLoaderOptions options)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(options);

        _images = images;
        _options = options;

        // A limit going up is the one change that can admit work without a fetch ending. The options
        // object notifies after releasing its own lock, and this subscribes once, at construction,
        // so there is no window in which a raised cap fails to reach a waiting handle.
        _options.LimitsRaised += NotifyAllWaiters;
    }

    /// <summary>
    /// How many requests this factory is holding, for the suite that proves a completed fetch leaves
    /// nothing behind. It is not public: an application has no business depending on the map.
    /// </summary>
    internal int InFlightRequestCount
    {
        get
        {
            lock (_sync)
            {
                return _fetches.Count;
            }
        }
    }

    /// <summary>
    /// The fetch a URI currently has, for the suite that proves the shared result drops its bytes.
    /// </summary>
    /// <param name="uri">The canonical URI to look up.</param>
    /// <returns>The in-flight fetch, or null when nothing is running for that URI.</returns>
    /// <remarks>
    /// A test holds this reference deliberately for longer than the loader does, which is exactly
    /// the case the retention rule has to survive: a leaked fetch must not keep an image file in
    /// memory after its handles have decoded it.
    /// </remarks>
    internal HttpAssetFetch? PeekFetch(string uri)
    {
        lock (_sync)
        {
            return _fetches.GetValueOrDefault(uri);
        }
    }

    /// <summary>
    /// How many capacity slots are held, for the suite that proves an aborting fetch keeps its slot
    /// until it unwinds.
    /// </summary>
    /// <remarks>
    /// Not the map's size: a fetch that has been aborted can be replaced under the same URI while it
    /// is still unwinding, and the replacement is a second request holding a second slot until the
    /// first one ends.
    /// </remarks>
    internal int InFlightSlotCount
    {
        get
        {
            lock (_sync)
            {
                return _totalInFlight;
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It claims an absolute <c>http</c> or <c>https</c> URI and nothing else, and a URI it does not
    /// claim is refused rather than thrown at: a malformed <c>http:</c> candidate is the next
    /// factory's chance to answer, and the provider's contract is a predicate rather than a
    /// validation pass (dev-docs/ASSET-LOADING.md section 2.1).
    /// </remarks>
    public bool TryCreateAssetLoader(string uri, [NotNullWhen(true)] out IAssetLoader<ImageData>? loader)
    {
        if (!TryParse(uri, out Uri? parsed, out string host))
        {
            loader = null;

            return false;
        }

        var handle = new HttpAssetLoader(this, parsed.AbsoluteUri, host);

        // A weak reference, because a handle the application made and never submitted must not be
        // kept alive by the factory's list of things it might need to notify. A handle the manager
        // holds is alive by definition; one it has dropped is collectable.
        lock (_sync)
        {
            _waiting.Add(new WeakReference<HttpAssetLoader>(handle));
        }

        loader = handle;

        return true;
    }

    /// <summary>Whether this handle could attach a request right now, without reserving anything.</summary>
    /// <param name="uri">The canonical URI the handle asks for.</param>
    /// <param name="host">The host its limit is counted against.</param>
    /// <returns>True when the handle would share a running fetch, or when both caps have room.</returns>
    internal bool CanLoadNow(string uri, string host)
    {
        lock (_sync)
        {
            // An entry that is aborting or finished cannot take another handle. Treating it as
            // absent is what stops the gate saying yes to a request that is already on its way out;
            // the replacement a load then starts is counted as a fresh request.
            if (_fetches.TryGetValue(uri, out HttpAssetFetch? running) && running.AcceptsHandles)
            {
                return true;
            }

            return _totalInFlight < _options.MaxConcurrentRequests
                && HostCount(host) < _options.MaxConcurrentRequestsPerHost;
        }
    }

    /// <summary>
    /// Attaches one handle to the URI's fetch, starting it when it is the first, and produces that
    /// handle's image.
    /// </summary>
    /// <param name="handle">The handle, so its waiting registration can be withdrawn.</param>
    /// <param name="uri">The canonical URI it asks for.</param>
    /// <param name="host">The host it is counted against.</param>
    /// <param name="cancellationToken">The caller's token, which stops this handle's wait.</param>
    /// <returns>The handle's own decoded image.</returns>
    /// <exception cref="InvalidOperationException">
    /// The gate was not consulted and there is no room. A caller that is not the manager has to ask
    /// <see cref="CanLoadNow"/> before submitting, because a load that starts over the cap is a
    /// contract violation rather than something to queue.
    /// </exception>
    internal Task<ImageData> LoadAsync(
        HttpAssetLoader handle,
        string uri,
        string host,
        CancellationToken cancellationToken)
    {
        HttpAssetFetch fetch;
        bool startsFetch;

        lock (_sync)
        {
            RemoveWaiter(handle);

            if (_fetches.TryGetValue(uri, out HttpAssetFetch? existing) && existing.TryAddHandle())
            {
                fetch = existing;
                startsFetch = false;
            }
            else
            {
                if (existing is not null)
                {
                    // The entry is aborting or finished and will not take another handle. Removing
                    // it here is what lets this call count a fresh request; the dying fetch's End
                    // must not remove the replacement (it checks identity before removing).
                    _fetches.Remove(uri);
                }

                // A replacement takes a new slot, so it can be refused for capacity while the dying
                // fetch still holds its own - which is correct: two requests really are in flight
                // until the first one unwinds.
                //
                // The count moves here, synchronously, before the task exists: a walk that starts
                // one request and immediately reads every gate sees this slot taken, which is what
                // stops it overshooting (section 4.2).
                if (_totalInFlight >= _options.MaxConcurrentRequests
                    || HostCount(host) >= _options.MaxConcurrentRequestsPerHost)
                {
                    throw new InvalidOperationException(
                        $"There is no capacity for '{uri}' right now. IAssetLoadManager asks CanLoadNow " +
                        "before it starts a request; a caller that submits a handle directly has to " +
                        "ask the same question first.");
                }

                fetch = new HttpAssetFetch(uri, host);
                _fetches.Add(uri, fetch);
                _totalInFlight++;
                _perHost[host] = HostCount(host) + 1;
                fetch.TryAddHandle();
                startsFetch = true;
            }
        }

        if (startsFetch)
        {
            // The request is started outside the lock: a message handler may call back into the
            // client, and nothing here is improved by making the handler wait behind the map.
            Start(fetch);
        }

        return DecodeAsync(fetch, cancellationToken);
    }

    /// <summary>Waits for the shared bytes, decodes this handle's own image, and releases its interest.</summary>
    /// <param name="fetch">The fetch this handle attached to.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The decoded image, which belongs to this handle's caller alone.</returns>
    /// <remarks>
    /// <para>
    /// The await deliberately does not say <c>ConfigureAwait(false)</c>: the manager calls this from
    /// the UI thread with the UI context installed, and the decode after it is UI-thread state - an
    /// animated image carries the clock its frames are computed from
    /// (dev-docs/ASSET-LOADING.md sections 3.4 and 3.12). On a thread with no context, which is a test
    /// or a headless host, the resume happens on whichever thread completed the fetch.
    /// </para>
    /// <para>
    /// The encoded bytes are a local: they are handed to the decoder, and the method returns without
    /// storing them anywhere. The shared result drops its reference when the last handle releases, so
    /// a load that produced an <see cref="ImageData"/> leaves no encoded copy behind. A caller that
    /// asked for bytes would be a different loader, and the future cache is the thing that decides to
    /// keep them.
    /// </para>
    /// </remarks>
    private async Task<ImageData> DecodeAsync(HttpAssetFetch fetch, CancellationToken cancellationToken)
    {
        var waiter = new TaskCompletionSource<HttpFetchResult>();
        int released = 0;

        void Release()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                fetch.ReleaseHandle();
            }
        }

        // ContinueWith with ExecuteSynchronously rather than WaitAsync(token): an ordinary completion
        // has to reach an awaiting handle on the thread that completed the fetch, not a thread-pool
        // turn later. The abandoned path is the exception - see below.
        var state = new DecodeState(waiter, cancellationToken);

        _ = fetch.Completion.ContinueWith(
            static (completed, target) =>
            {
                var decode = (DecodeState)target!;

                if (Volatile.Read(ref decode.Abandoned) != 0)
                {
                    // The caller is already answered and the late result goes nowhere. The end is
                    // posted rather than completed here so that a cancellation callback is not doing
                    // completion work on a caller's behalf - the callback's job is to mark the handle
                    // and give its interest back. The manager's answer no longer depends on this
                    // either way (section 3.9 answers a cancelled request from its own token), so the
                    // hop is a rule about where work happens, not a fix for an ordering hazard.
                    ThreadPool.QueueUserWorkItem(
                        static queued =>
                        {
                            var target = (DecodeState)queued!;
                            target.Completion.TrySetCanceled(target.Token);
                        },
                        decode);

                    return;
                }

                if (completed.IsCompletedSuccessfully)
                {
                    decode.Completion.TrySetResult(completed.Result);
                }
                else
                {
                    decode.Completion.TrySetException(completed.Exception!);
                }
            },
            state,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            Volatile.Write(ref state.Abandoned, 1);
            Release();
        });

        try
        {
            HttpFetchResult result = await state.Completion.Task;

            cancellationToken.ThrowIfCancellationRequested();

            result.ThrowIfFailed();

            byte[] bytes = result.Bytes ?? throw new InvalidOperationException(
                $"The request for '{fetch.Uri}' produced no bytes and no failure.");

            return _images.LoadFromBytes(bytes);
        }
        finally
        {
            // After the decode, so the last release is the point at which the shared array stops
            // being useful; on cancellation the release is what may abort the request.
            Release();
        }
    }

    /// <summary>The per-handle state a decode shares between its continuation and its cancellation.</summary>
    private sealed class DecodeState
    {
        public DecodeState(TaskCompletionSource<HttpFetchResult> completion, CancellationToken token)
        {
            Completion = completion;
            Token = token;
        }

        /// <summary>The result of the decode, pending until the fetch ends or this handle is abandoned.</summary>
        public TaskCompletionSource<HttpFetchResult> Completion { get; }

        /// <summary>The caller's token, carried so an abandoned load ends with the cancellation it asked for.</summary>
        public CancellationToken Token { get; }

        /// <summary>Non-zero once the caller cancelled, so the late result is dropped rather than decoded.</summary>
        public int Abandoned;
    }

    /// <summary>Starts one fetch. Every outcome is handled inside, so nothing observes this task.</summary>
    /// <param name="fetch">The fetch to run.</param>
    private void Start(HttpAssetFetch fetch) => _ = FetchAsync(fetch);

    /// <summary>Performs the request, publishes its outcome, and releases its capacity exactly once.</summary>
    /// <param name="fetch">The fetch to run.</param>
    /// <remarks>
    /// <para>
    /// The outcome is a value rather than a faulted task on purpose: the only thing that observes this
    /// task is the runtime's unobserved-exception machinery, and every caller has already been
    /// answered through <see cref="HttpAssetFetch.Completion"/>. A fault here would be an exception
    /// nobody could handle.
    /// </para>
    /// <para>
    /// The capacity release is in the <c>finally</c> and runs on the thread the request finished on,
    /// whether it succeeded, failed, timed out or was aborted. It happens before the handles are
    /// completed, so a caller that immediately asks for the same URI again - which the manager's
    /// re-entrancy rules make possible inside the completion - starts a fresh fetch rather than
    /// attaching to a finished one (section 3.8).
    /// </para>
    /// </remarks>
    private async Task FetchAsync(HttpAssetFetch fetch)
    {
        // Definitely assigned for the compiler; the catch below replaces it on every failure, so no
        // caller can ever observe this instance.
        HttpFetchResult result = HttpFetchResult.Failed(
            new InvalidOperationException($"The request for '{fetch.Uri}' ended without an outcome."));

        try
        {
            result = HttpFetchResult.Succeeded(await DownloadAsync(fetch).ConfigureAwait(false));
        }
        catch (Exception error)
        {
            // Every exception, including an out-of-memory one: callers are waiting on this fetch, and
            // the one outcome that must not happen is a task that never ends because a failure was
            // filtered out.
            result = HttpFetchResult.Failed(error);
        }
        finally
        {
            WaitersToWake waiters = End(fetch, out string host);

            fetch.Complete(result);
            Wake(waiters, host);
        }
    }

    /// <summary>Sends the request and reads the body.</summary>
    /// <param name="fetch">The fetch whose URI and abort token are used.</param>
    /// <returns>The encoded image.</returns>
    /// <exception cref="HttpRequestException">The response was not a success status.</exception>
    /// <exception cref="TimeoutException">The fetch's timeout aborted it while handles waited.</exception>
    private async Task<byte[]> DownloadAsync(HttpAssetFetch fetch)
    {
        HttpClient client = EnsureClient(out bool isOurs);

        using var request = new HttpRequestMessage(HttpMethod.Get, fetch.Uri);

        if (isOurs)
        {
            // The application's own client keeps its own default headers; ours carries the product
            // token the server would otherwise not see.
            string? userAgent = _options.UserAgent;

            if (userAgent is not null)
            {
                request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            }
        }

        TimeSpan timeout = _options.Timeout;

        if (timeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            fetch.StartTimeout(timeout);
        }

        try
        {
            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, fetch.AbortToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync(fetch.AbortToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (fetch.TimeoutArmed && fetch.HasWaiters)
        {
            // A caller's cancellation never gets here - each handle stops its own wait - and the
            // only other thing that cancels this token is ReleaseHandle at zero handles. So a
            // cancelled request with a handle still waiting is the deadline this fetch armed.
            throw new TimeoutException(
                $"The request for '{fetch.Uri}' did not complete within {timeout}.");
        }
    }

    /// <summary>
    /// Chooses the transport once, on the first fetch, and never changes it afterwards.
    /// </summary>
    /// <param name="isOurs">
    /// True when this factory created the client, which is what decides whether it may add its own
    /// headers and set the client's timeout out of the way.
    /// </param>
    /// <returns>The client to send with.</returns>
    /// <exception cref="InvalidOperationException">
    /// Both <see cref="HttpAssetLoaderOptions.HttpClient"/> and
    /// <see cref="HttpAssetLoaderOptions.MessageHandler"/> are set, so there is no single transport to
    /// choose.
    /// </exception>
    /// <remarks>
    /// The choice is made once because a message handler carries in-flight requests; swapping one out
    /// under them would lose them, and there is no configuration where doing so is what an application
    /// meant. It is made lazily rather than at construction because the options object is mutable and
    /// an application may set the transport after registering.
    /// </remarks>
    private HttpClient EnsureClient(out bool isOurs)
    {
        lock (_sync)
        {
            if (_client is not null)
            {
                isOurs = _ownsClient;
                return _client;
            }

            HttpClient? supplied = _options.HttpClient;
            HttpMessageHandler? handler = _options.MessageHandler;

            if (supplied is not null && handler is not null)
            {
                throw new InvalidOperationException(
                    "HttpAssetLoaderOptions has both HttpClient and MessageHandler set. Set one of " +
                    "them: a client is used as it is, and a handler is wrapped in a client this " +
                    "loader owns. With both there is no way to tell which transport was meant.");
            }

            if (supplied is not null)
            {
                _client = supplied;
                _ownsClient = false;
                isOurs = false;
            }
            else
            {
                _client = handler is null
                    ? new HttpClient()
                    : new HttpClient(handler, disposeHandler: false);

                // The per-request deadline is the loader's, and it is applied through the fetch's
                // token. Leaving HttpClient's own 100-second timer armed would mean two deadlines
                // and the longer one deciding.
                _client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
                _ownsClient = true;
                isOurs = true;
            }

            return _client;
        }
    }

    /// <summary>
    /// Removes a finished fetch from the in-flight map, gives its slots back, and says who has to be
    /// asked to try again.
    /// </summary>
    /// <param name="fetch">The fetch that ended.</param>
    /// <param name="host">The host whose slot was released.</param>
    /// <returns>The set of waiting handles that could have been blocked by what just freed.</returns>
    private WaitersToWake End(HttpAssetFetch fetch, out string host)
    {
        host = fetch.Host;

        WaitersToWake waiters;

        lock (_sync)
        {
            // Read before the decrement: these are the caps that were binding while the fetch ran.
            bool totalWasFull = _totalInFlight >= _options.MaxConcurrentRequests;
            bool hostWasFull = HostCount(fetch.Host) >= _options.MaxConcurrentRequestsPerHost;

            _totalInFlight--;

            int remaining = HostCount(fetch.Host) - 1;

            if (remaining == 0)
            {
                _perHost.Remove(fetch.Host);
            }
            else
            {
                _perHost[fetch.Host] = remaining;
            }

            if (_fetches.TryGetValue(fetch.Uri, out HttpAssetFetch? current) && ReferenceEquals(current, fetch))
            {
                // Load-bearing, not defensive: an aborting fetch can be replaced under the same URI
                // before it unwinds, and this is what keeps the dying fetch from removing its
                // replacement's entry.
                _fetches.Remove(fetch.Uri);
            }

            waiters = totalWasFull
                ? WaitersToWake.Everyone
                : hostWasFull
                    ? WaitersToWake.Host
                    : WaitersToWake.Nobody;
        }

        return waiters;
    }

    /// <summary>Tells the handles that could have been blocked by a freed slot to ask again.</summary>
    /// <param name="waiters">The scope <see cref="End"/> computed.</param>
    /// <param name="host">The host whose slot freed, used when the scope is that host.</param>
    private void Wake(WaitersToWake waiters, string host)
    {
        switch (waiters)
        {
            case WaitersToWake.Everyone:
                NotifyAllWaiters();
                break;

            case WaitersToWake.Host:
                NotifyHostWaiters(host);
                break;
        }
    }

    /// <summary>Tells every handle that is still waiting that its gate may have opened.</summary>
    private void NotifyAllWaiters()
    {
        HttpAssetLoader[] waiters;

        lock (_sync)
        {
            waiters = SnapshotWaitersLocked(host: null);
        }

        foreach (HttpAssetLoader waiter in waiters)
        {
            waiter.RaiseCanLoadNowChanged();
        }
    }

    /// <summary>Tells the still-waiting handles for one host that its gate may have opened.</summary>
    /// <param name="host">The host whose slot freed.</param>
    private void NotifyHostWaiters(string host)
    {
        HttpAssetLoader[] waiters;

        lock (_sync)
        {
            waiters = SnapshotWaitersLocked(host);
        }

        foreach (HttpAssetLoader waiter in waiters)
        {
            waiter.RaiseCanLoadNowChanged();
        }
    }

    /// <summary>
    /// Copies the live waiting handles, compacting dead weak references out while it walks.
    /// </summary>
    /// <param name="host">The host to filter to, or null for every host.</param>
    /// <returns>The handles to notify.</returns>
    private HttpAssetLoader[] SnapshotWaitersLocked(string? host)
    {
        var alive = new List<HttpAssetLoader>(_waiting.Count);
        int kept = 0;

        for (int read = 0; read < _waiting.Count; read++)
        {
            WeakReference<HttpAssetLoader> reference = _waiting[read];

            if (!reference.TryGetTarget(out HttpAssetLoader? handle))
            {
                continue;
            }

            _waiting[kept++] = reference;

            if (host is null || handle.Host == host)
            {
                alive.Add(handle);
            }
        }

        if (kept < _waiting.Count)
        {
            _waiting.RemoveRange(kept, _waiting.Count - kept);
        }

        return [.. alive];
    }

    /// <summary>Withdraws a handle from the waiting list, because it is no longer waiting.</summary>
    /// <param name="handle">The handle that is starting.</param>
    private void RemoveWaiter(HttpAssetLoader handle)
    {
        for (int index = 0; index < _waiting.Count; index++)
        {
            if (_waiting[index].TryGetTarget(out HttpAssetLoader? candidate)
                && ReferenceEquals(candidate, handle))
            {
                _waiting.RemoveAt(index);
                return;
            }
        }
    }

    /// <summary>The number of fetches currently counted against one host.</summary>
    /// <param name="host">The normalized host.</param>
    /// <returns>The count, or zero when the host has nothing in flight.</returns>
    private int HostCount(string host) => _perHost.GetValueOrDefault(host);

    /// <summary>Parses a URI and answers whether this factory claims it.</summary>
    /// <param name="uri">What was asked for.</param>
    /// <param name="parsed">The canonical absolute URI, on success.</param>
    /// <param name="host">The host key the per-host limit uses, on success.</param>
    /// <returns>True when the URI is an absolute http or https URI.</returns>
    private static bool TryParse(string? uri, [NotNullWhen(true)] out Uri? parsed, out string host)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out parsed)
            || !(string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            || parsed.Host.Length == 0)
        {
            host = string.Empty;

            return false;
        }

        // The host, not the origin: the same server is one server whatever its scheme or port, and
        // IdnHost is the same spelling for an internationalized name whoever typed it.
        host = parsed.IdnHost.ToLowerInvariant();

        return true;
    }

    /// <summary>Which waiting handles a finished fetch could have unblocked.</summary>
    private enum WaitersToWake
    {
        /// <summary>Neither cap was binding, so nothing was waiting on this fetch.</summary>
        Nobody,

        /// <summary>The host's cap was binding, so that host's waiters are told.</summary>
        Host,

        /// <summary>The total cap was binding, so every waiter is told.</summary>
        Everyone,
    }
}

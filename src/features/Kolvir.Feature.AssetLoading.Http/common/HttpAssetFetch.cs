namespace Kolvir.Feature.AssetLoading.Http;

/// <summary>
/// One fetch of one URI, shared by every handle that asked for that URI while it was in flight.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shared state holds bytes.</b> There is no <see cref="ImageData"/> in this type, and there
/// must never be one: each handle decodes its own image from the bytes, so the manager's rule that a
/// result belongs to one caller stays true even though the request does not
/// (dev-docs/ASSET-LOADING.md section 4.1).
/// </para>
/// <para>
/// <b>The bytes live exactly as long as a handle may still decode them.</b> The handle count is taken
/// when a handle attaches, and the last handle to finish decoding drops the array
/// (<see cref="ReleaseHandle"/>). If every handle cancels before the request ends, the count already
/// reached zero and the abort is raised; the response that arrives afterwards has nobody to hand
/// bytes to and is dropped with this object.
/// </para>
/// <para>
/// <b>The completion is deliberately synchronous.</b> It is a plain
/// <see cref="TaskCompletionSource{TResult}"/> without
/// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>, so a handle that is already
/// awaiting resumes inside the call that completed the fetch, exactly as the manager's own request
/// does (section 3.8): the caller's task is ready when the response is, rather than a thread-pool turn
/// later. In an application the handle captured the UI context, so the decode still runs there; in a
/// test with no context it runs on the thread that supplied the response, which is what keeps the
/// suite deterministic without a sleep or a timer.
/// </para>
/// </remarks>
internal sealed class HttpAssetFetch
{
    private readonly CancellationTokenSource _abort = new();
    private readonly TaskCompletionSource<HttpFetchResult> _completion = new();

    private volatile HttpFetchResult? _result;
    private int _handles;
    private int _finished;
    private int _timedOut;

    /// <summary>One fetch, for one canonical URI and its host.</summary>
    /// <param name="uri">The canonical absolute URI this fetch asks for.</param>
    /// <param name="host">The host the capacity was counted against.</param>
    public HttpAssetFetch(string uri, string host)
    {
        Uri = uri;
        Host = host;
    }

    /// <summary>The canonical absolute URI every handle over this fetch asked for.</summary>
    public string Uri { get; }

    /// <summary>The host this fetch occupies a per-host slot for.</summary>
    public string Host { get; }

    /// <summary>The outcome, once there is one. It never faults; failures are in the result.</summary>
    public Task<HttpFetchResult> Completion => _completion.Task;

    /// <summary>The token the HTTP request is sent with: this fetch's own, not any caller's.</summary>
    public CancellationToken AbortToken => _abort.Token;

    /// <summary>True when the fetch was aborted by its own timeout rather than by the last handle.</summary>
    public bool TimedOut => Volatile.Read(ref _timedOut) != 0;

    /// <summary>True while a handle is still waiting on this fetch's outcome.</summary>
    public bool HasWaiters => Volatile.Read(ref _handles) > 0;

    /// <summary>Registers one handle's interest in the bytes.</summary>
    public void AddHandle() => Interlocked.Increment(ref _handles);

    /// <summary>
    /// Starts the fetch's own timeout. Called once, before the request is sent, because a fetch has
    /// one deadline rather than one per handle.
    /// </summary>
    /// <param name="timeout">How long the request may take.</param>
    public void StartTimeout(TimeSpan timeout)
    {
        Volatile.Write(ref _timedOut, 1);
        _abort.CancelAfter(timeout);
    }

    /// <summary>
    /// Publishes the outcome to every waiting handle, and drops the bytes if nobody is left to
    /// decode them.
    /// </summary>
    /// <param name="result">What the request produced.</param>
    public void Complete(HttpFetchResult result)
    {
        _result = result;
        Volatile.Write(ref _finished, 1);

        // Stops the timeout's timer without disposing the source: a handle that is still unwinding
        // may read AbortToken, and CancelAfter(Infinite) is the cancellable way to release the timer.
        _abort.CancelAfter(System.Threading.Timeout.InfiniteTimeSpan);

        _completion.TrySetResult(result);

        if (Volatile.Read(ref _handles) == 0)
        {
            result.Release();
        }
    }

    /// <summary>
    /// Releases one handle's interest, and aborts the request when it was the last one.
    /// </summary>
    /// <remarks>
    /// The release runs after the handle has decoded, so the last release is the point at which the
    /// encoded bytes stop being useful; from there the array is dropped even if this object is still
    /// referenced. Cancelling the request is the last handle's job only: an earlier handle leaving
    /// must not disturb a fetch somebody else is still waiting on.
    /// </remarks>
    public void ReleaseHandle()
    {
        if (Interlocked.Decrement(ref _handles) > 0)
        {
            return;
        }

        _result?.Release();

        if (Volatile.Read(ref _finished) == 0)
        {
            _abort.Cancel();
        }
    }
}

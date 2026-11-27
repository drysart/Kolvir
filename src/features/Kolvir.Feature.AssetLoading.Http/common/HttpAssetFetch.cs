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
/// <b>An aborted fetch takes no more handles.</b> The abort does not end the request immediately -
/// unwinding takes as long as the transport takes - and the factory's map still has this entry until
/// <c>End</c> runs. <see cref="TryAddHandle"/> is the atomic answer a new handle needs: attaching to a
/// fetch whose abort token is already cancelled would hand the caller a request that was never given
/// a chance to succeed. <see cref="AcceptsHandles"/> is the same question without attaching, for a
/// gate that must not say yes to it.
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
    private readonly object _sync = new();
    private readonly CancellationTokenSource _abort = new();
    private readonly TaskCompletionSource<HttpFetchResult> _completion = new();

    private HttpFetchResult? _result;
    private int _handles;
    private bool _finished;
    private bool _aborted;
    private bool _timeoutArmed;

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

    /// <summary>
    /// True when this fetch armed a deadline of its own, whether or not that deadline has fired.
    /// </summary>
    /// <remarks>
    /// It records that <see cref="StartTimeout"/> ran, not that the timer was the thing that
    /// cancelled the request. The throw site in the factory is where the two are told apart: the only
    /// other thing that cancels a fetch is the last handle leaving, and that happens at zero handles -
    /// so a cancellation with a handle still waiting is the deadline.
    /// </remarks>
    public bool TimeoutArmed
    {
        get
        {
            lock (_sync)
            {
                return _timeoutArmed;
            }
        }
    }

    /// <summary>True while a handle is still waiting on this fetch's outcome.</summary>
    public bool HasWaiters
    {
        get
        {
            lock (_sync)
            {
                return _handles > 0;
            }
        }
    }

    /// <summary>True when this fetch can still take a handle.</summary>
    /// <remarks>
    /// False once it finished or once its last handle left and cancelled the request. The gate reads
    /// this together with the counts, so a request already on its way out is not offered to a new
    /// caller as something to share.
    /// </remarks>
    public bool AcceptsHandles
    {
        get
        {
            lock (_sync)
            {
                return !_finished && !_aborted;
            }
        }
    }

    /// <summary>
    /// Registers one handle's interest, unless the fetch is finished or already aborting.
    /// </summary>
    /// <returns>True when the handle was attached; false when it has to start a fetch of its own.</returns>
    /// <remarks>
    /// The test and the increment are one operation under one lock, which is what closes the race
    /// between a new handle arriving and the last one leaving: a handle cannot attach to a fetch that
    /// has just decided to abort, and an abort cannot decide while a handle is attaching.
    /// </remarks>
    public bool TryAddHandle()
    {
        lock (_sync)
        {
            if (_finished || _aborted)
            {
                return false;
            }

            _handles++;

            return true;
        }
    }

    /// <summary>
    /// Starts the fetch's own timeout. Called once, before the request is sent, because a fetch has
    /// one deadline rather than one per handle.
    /// </summary>
    /// <param name="timeout">How long the request may take.</param>
    /// <remarks>
    /// The interval is armed as a cancellation of this fetch's token, so the transport's own
    /// cancellation path ends the request; the flag only records that the deadline exists.
    /// </remarks>
    public void StartTimeout(TimeSpan timeout)
    {
        lock (_sync)
        {
            _timeoutArmed = true;
        }

        _abort.CancelAfter(timeout);
    }

    /// <summary>
    /// Publishes the outcome to every waiting handle, and drops the bytes if nobody is left to
    /// decode them.
    /// </summary>
    /// <param name="result">What the request produced.</param>
    public void Complete(HttpFetchResult result)
    {
        bool releaseNow;

        lock (_sync)
        {
            _result = result;
            _finished = true;
            releaseNow = _handles == 0;
        }

        // Stops the timeout's timer without disposing the source: a handle that is still unwinding
        // may read AbortToken, and CancelAfter(Infinite) is the cancellable way to release the timer.
        _abort.CancelAfter(System.Threading.Timeout.InfiniteTimeSpan);

        // Outside the lock: a continuation runs the decode and the manager's bookkeeping, and may
        // re-enter the factory. Holding this fetch's lock here would invert the factory's lock order.
        _completion.TrySetResult(result);

        if (releaseNow)
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
    /// must not disturb a fetch somebody else is still waiting on. The abort is raised outside the
    /// lock because its callbacks belong to the transport and may re-enter the factory.
    /// </remarks>
    public void ReleaseHandle()
    {
        bool abort;

        lock (_sync)
        {
            if (--_handles > 0)
            {
                return;
            }

            _result?.Release();
            _aborted = !_finished;
            abort = _aborted;
        }

        if (abort)
        {
            _abort.Cancel();
        }
    }
}

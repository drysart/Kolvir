namespace Kolvir.Feature.AssetLoading;

/// <summary>
/// One request the manager is holding: the handle, where it sits in the queue, and everything that
/// has to be released when it leaves.
/// </summary>
/// <remarks>
/// <para>
/// The manager holds these, not loaders, because a request is more than its handle: the sequence
/// number, the phase, the two subscriptions and the caller's completion all belong to the request
/// and none of them belong on the loader - a loader is a handle an application can make cheaply and
/// the manager must not add to (dev-docs/ASSET-LOADING.md section 4.1).
/// </para>
/// <para>
/// The completion is generic in the derived type so a caller's <c>await</c> is typed. This base is
/// what the queue, the dictionary and the walk work with, which is why they never mention a result
/// type at all.
/// </para>
/// </remarks>
internal abstract class AssetLoadRequest
{
    protected AssetLoadRequest(IAssetLoader loader, Action? onStarted, long sequence, CancellationToken cancellationToken)
    {
        Loader = loader;
        OnStarted = onStarted;
        Sequence = sequence;
        CancellationToken = cancellationToken;
    }

    /// <summary>The handle this request is for.</summary>
    public IAssetLoader Loader { get; }

    /// <summary>The caller's started callback, raised once when the loader is asked.</summary>
    public Action? OnStarted { get; }

    /// <summary>Submission order, kept across a priority change.</summary>
    public long Sequence { get; }

    /// <summary>The token the loader is handed when the walk starts this request.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Queued until the walk starts this request, then running.</summary>
    public AssetLoadPhase Phase { get; set; }

    /// <summary>True once the caller has been answered or has asked to be.</summary>
    public bool CallerDone { get; set; }

    /// <summary>
    /// True once the request has left the manager. Set before anything is completed or released, so a
    /// re-entrant cancellation or completion finds a request it can ignore.
    /// </summary>
    public bool IsFinished { get; set; }

    /// <summary>The loader's priority subscription, removed on every exit path.</summary>
    public IDisposable? PrioritySubscription { get; set; }

    /// <summary>
    /// The loader's availability subscription. Removed with the priority one except for a request
    /// cancelled while running, which keeps it until the underlying task ends because the slot it
    /// occupies is still taken until then (dev-docs/ASSET-LOADING.md section 3.9).
    /// </summary>
    public IDisposable? CanLoadNowSubscription { get; set; }

    /// <summary>The caller's cancellation registration, released on every exit path.</summary>
    public CancellationTokenRegistration CancellationRegistration { get; set; }

    /// <summary>
    /// True once the caller's task has been completed with a value, which is the manager's answer to
    /// "did anyone receive this result".
    /// </summary>
    /// <remarks>
    /// Read from the task's status rather than from <see cref="HandOver"/>'s return: committing the
    /// value and handing it over are one call, and only the task's status survives everything that
    /// call may do. (A caller's continuation throwing does not come back out of it - the runtime
    /// captures that and rethrows it asynchronously - but the manager's answer should not rest on
    /// which exceptions a completion chooses to swallow.)
    /// <see cref="TaskStatus.RanToCompletion"/> rather than <see cref="Task.IsCompleted"/>, because
    /// <see cref="Cancel"/> and <see cref="Fail"/> complete the task too and neither accepted a value.
    /// </remarks>
    public abstract bool Delivered { get; }

    /// <summary>Hands the loaded value to the caller.</summary>
    /// <param name="result">What the loader produced.</param>
    /// <returns>
    /// True when the caller received it. False when the caller had already been answered - a
    /// cancellation arriving from another thread at the same moment is the case - which leaves the
    /// value with nobody holding it. The manager releases on the same condition but asks
    /// <see cref="Delivered"/> for it, so that nothing a completion does can lose the answer.
    /// </returns>
    /// <exception cref="InvalidCastException">
    /// <paramref name="result"/> is not a <c>TResult</c>. A factory bug; the manager fails the caller
    /// with it rather than leaving it waiting.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="result"/> is null for a value type, so it cannot be unboxed. Also a factory bug,
    /// and phrased around the loader because the runtime's own exception names neither type.
    /// </exception>
    public abstract bool HandOver(object? result);

    /// <summary>Answers the caller with the failure.</summary>
    /// <param name="error">What the load threw.</param>
    public abstract void Fail(Exception error);

    /// <summary>Answers the caller as cancelled.</summary>
    public abstract void Cancel();

    /// <summary>Removes both handler subscriptions, tolerating one that is already gone.</summary>
    public void DetachHandlers()
    {
        DetachPriorityHandler();
        DetachCanLoadNowHandler();
    }

    /// <summary>Removes the priority subscription and forgets it.</summary>
    public void DetachPriorityHandler()
    {
        IDisposable? subscription = PrioritySubscription;
        PrioritySubscription = null;
        subscription?.Dispose();
    }

    /// <summary>Removes the availability subscription and forgets it.</summary>
    public void DetachCanLoadNowHandler()
    {
        IDisposable? subscription = CanLoadNowSubscription;
        CanLoadNowSubscription = null;
        subscription?.Dispose();
    }

    /// <summary>
    /// Releases the cancellation registration and forgets it. Disposing a default registration is a
    /// no-op, and disposing one whose callback is already running on this thread does not wait for
    /// itself, so every exit path can call this without knowing which path it is.
    /// </summary>
    public void DetachCancellation()
    {
        CancellationTokenRegistration registration = CancellationRegistration;
        CancellationRegistration = default;
        registration.Dispose();
    }
}

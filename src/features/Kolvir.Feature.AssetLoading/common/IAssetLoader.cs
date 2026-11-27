namespace Kolvir.Feature.AssetLoading;

/// <summary>
/// One consumer's handle for one request: what it wants loaded, how urgently, and how to stop.
/// </summary>
/// <remarks>
/// <para>
/// A loader is not a fetch. It is the per-consumer half of one - it carries that consumer's
/// <see cref="Priority"/> and that consumer's cancellation token - and it is cheap to make. Two
/// controls that want the same bytes get two handles, not one: sharing one instance would make them
/// fight over a single priority and a single cancellation, so one control being disposed would
/// cancel the other's image (dev-docs/ASSET-LOADING.md section 4.1). The sharing that saves the
/// bytes belongs underneath, in the factory that wrote the URI logic, which refcounts one fetch and
/// completes every handle held over it.
/// </para>
/// <para>
/// An instance is used by one request at a time. <see cref="IAssetLoadManager"/> rejects a second
/// submission of a live instance and accepts one after the first has finished (section 3.11), which
/// is the rule that lets a handle be reused without two requests ever sharing it.
/// </para>
/// <para>
/// Everything on this interface is UI-thread state, because it is read by the manager's walk: a
/// loader is created, prioritised and cancelled on the thread that owns the controls it is asked
/// for. What the loader does inside <see cref="LoadAsync"/> - an HTTP request, a file read - runs
/// wherever it likes.
/// </para>
/// </remarks>
public interface IAssetLoader
{
    /// <summary>
    /// How urgent this request is. Higher wins: the manager walks the queue in descending priority,
    /// and equal priorities keep submission order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is settable because the set of visible controls changes while loads are queued - a scroll,
    /// a tab switch - so a priority has to be changeable after the request was made. Setting it
    /// moves the request in the queue and nothing else: a running load is never preempted, because
    /// no loader contract here carries a resumable position and a restart on a scroll would throw
    /// away bytes the application already paid for (section 3.5).
    /// </para>
    /// <para>
    /// What a loader starts at is that loader's business; this package ships no reference loader and
    /// no default. <see cref="AssetLoadPriority"/> supplies two anchors for the integration rather
    /// than a default for you, and the safe direction for an unclassified load is
    /// <see cref="AssetLoadPriority.Background"/>, because competing with what the user is looking
    /// at is the one mistake this feature exists to prevent.
    /// </para>
    /// <para>
    /// Raising <see cref="AddPriorityChangedHandler"/> is what tells the manager its queue order is
    /// stale; setting the property silently leaves the queue ordered by the old value.
    /// </para>
    /// </remarks>
    int Priority { get; set; }

    /// <summary>Subscribes a handler that is told whenever <see cref="Priority"/> changed.</summary>
    /// <param name="handler">The handler to add. It receives this loader, so one dispatch path can
    /// route several loaders' notifications.</param>
    /// <returns>A token that removes the handler when it is disposed.</returns>
    /// <remarks>
    /// <para>
    /// The manager subscribes one of these for each request it holds, and removes it on every exit
    /// path - completion, cancellation, failure (section 3.10) - so an implementation only has to
    /// raise it on a real change and forget about the queue.
    /// </para>
    /// <para>
    /// <b>It may be raised from any thread.</b> A loader's own work runs wherever it runs, so a
    /// priority that a background step changes is raised from that background thread; the manager
    /// moves the notification onto the UI thread before it touches its queue, so raising it there is
    /// safe. Raising it on the UI thread when you can simply skips that post.
    /// </para>
    /// <para>
    /// A handler pair rather than an <c>event</c>, so a host that already funnels
    /// <see cref="Action{Object}"/> callbacks through one dispatch path can do the same for this,
    /// and so the virtual DOM generator recognises it as an event property (section 3.13).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
    IDisposable AddPriorityChangedHandler(Action<object> handler);

    /// <summary>Removes a handler added by <see cref="AddPriorityChangedHandler"/>.</summary>
    /// <param name="handler">The handler to remove. A handler that was never added is ignored.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
    void RemovePriorityChangedHandler(Action<object> handler);

    /// <summary>
    /// Whether a load may start right now, according to whoever knows - a capacity the factory is
    /// holding, a memory budget, or nothing at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The manager asks this and applies no rule of its own (section 3.2). A <see langword="false"/>
    /// vetoes this request for this walk and says nothing about the next one, which is why a vetoed
    /// request does not stop a lower-priority one from starting.
    /// </para>
    /// <para>
    /// It must describe capacity <em>now</em> and reserve nothing: asking does not consume a slot,
    /// two asks with no load started in between give the same answer, and the count a limit is
    /// enforced against moves when <see cref="LoadAsync"/> returns its task rather than at the first
    /// byte (section 4.2). A gate that reserved on request would hand the slot to whoever asked
    /// first, so a background load would start ahead of a visible one and the priority queue would
    /// be decoration.
    /// </para>
    /// <para>
    /// The manager reads it once per queued request per walk, and a walk happens on enqueue and on a
    /// notification. It never watches this on a timer (section 3.7).
    /// </para>
    /// </remarks>
    bool CanLoadNow { get; }

    /// <summary>Subscribes a handler that is told to ask <see cref="CanLoadNow"/> again.</summary>
    /// <param name="handler">The handler to add. It receives this loader.</param>
    /// <returns>A token that removes the handler when it is disposed.</returns>
    /// <remarks>
    /// <para>
    /// The notification means "ask again", not "here is your slot". A factory raises it on the
    /// request whose own capacity opened - its own domain, its own format - rather than on every
    /// queued request, or every scroll would re-walk the whole queue for a slot that was never
    /// theirs (section 4.2).
    /// </para>
    /// <para>
    /// <b>It is expected to be raised from the thread that finished a fetch.</b> An <c>https</c>
    /// loader completes on a pool thread, decrements its per-domain count and says so from there, and
    /// that is the contract working as designed: the manager moves the notification onto the UI
    /// thread itself, so a factory may raise it from wherever the count moved. Raising it on the UI
    /// thread when you can simply skips that post.
    /// </para>
    /// <para>
    /// The manager subscribes one of these per request and removes it on every exit path, with one
    /// deliberate exception it keeps for itself: a request cancelled while running keeps this
    /// subscription until the underlying task actually ends, because the slot is still occupied
    /// until then (section 3.9).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
    IDisposable AddCanLoadNowChangedHandler(Action<object> handler);

    /// <summary>Removes a handler added by <see cref="AddCanLoadNowChangedHandler"/>.</summary>
    /// <param name="handler">The handler to remove. A handler that was never added is ignored.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
    void RemoveCanLoadNowChangedHandler(Action<object> handler);

    /// <summary>Starts the load. Called by the manager when its walk selects this handle.</summary>
    /// <param name="cancellationToken">
    /// The token of the caller that submitted this handle. Honour it if the work can be stopped and
    /// ignore it if it cannot - a loader over an already-open stream has nothing to cancel - because
    /// the manager does not depend on it being honoured (section 3.9): the caller is answered when
    /// the cancellation arrives, whatever the loader does next.
    /// </param>
    /// <returns>
    /// A task that completes when the load has produced its result, with that result, or faults with
    /// the reason it failed. The manager observes it and hands the outcome to the caller; the task
    /// is also what tells the manager the load has ended, so a task that never completes is a
    /// request that never leaves the running state.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This member is what the manager calls, because the manager holds an <see cref="IAssetLoader"/>.
    /// It is not the member an application is invited to call: <see cref="IAssetLoader{TResult}"/>
    /// redeclares it with a typed result, which is what a caller's <c>await</c> gets
    /// (section 3.14). The untyped form boxes a value result to satisfy this signature; that is
    /// free for a class result such as <c>ImageData</c> and is not free for a struct, and no loader
    /// of a struct result is planned.
    /// </para>
    /// <para>
    /// Called on the UI thread, from inside the caller's <c>LoadAsync</c> call. The task may be
    /// already completed when it is returned - a cache hit, an embedded resource already in memory -
    /// and the manager handles that: it is the reason the walk is re-entrancy safe (section 3.8).
    /// It may also be completed from any thread, which is what a fetch does; the manager moves the
    /// outcome onto the UI thread before it touches its queue, so finishing on a pool thread is
    /// expected rather than a violation.
    /// </para>
    /// </remarks>
    Task<object?> LoadAsync(CancellationToken cancellationToken);
}

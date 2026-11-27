namespace Kolvir;

/// <summary>
/// The UI thread's <see cref="SynchronizationContext"/>, so an <c>await</c> in application code
/// resumes on the thread that owns the controls it is about to touch.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the model otherwise has none. Without a context installed, a continuation
/// resumes on whichever thread completed the task it awaited - a thread pool thread, for anything an
/// application awaited - and the first thing such a continuation does to a control is a cross-thread
/// violation. <see cref="Application.Run()"/> installs this context for the duration of the loop and
/// puts the previous one back when the loop ends. It is internal because application code needs
/// nothing but <see cref="SynchronizationContext.Current"/>, which is already public, and the model
/// gains no public member for it.
/// </para>
/// <para>
/// Every dispatch is one the model already owns: <see cref="Post"/> queues through
/// <see cref="Application.BeginInvoke"/> and <see cref="Send"/> goes through
/// <see cref="Application.Invoke"/>. What that covers is a continuation that is <em>dispatched</em>
/// through this context: it is queued rather than run inline, so it cannot overtake work already
/// queued, and an exception it throws reaches
/// <see cref="Application.AddUnhandledExceptionHandler"/> instead of unwinding into the platform's
/// message pump. It does not cover every <c>await</c> in application code. An awaiter runs its
/// continuation inline when the awaited task completes on a thread where this same instance is
/// current - inside a turn the loop is already running, for one - and then nothing reaches
/// <see cref="Post"/>: the continuation runs ahead of whatever is queued, and an exception it throws
/// unwinds into the completing code rather than the handler. Which of the two a given <c>await</c>
/// gets is the awaiter's decision rather than this type's; the asset loading note works the case that
/// depends on it through, in section 3.12.
/// </para>
/// <para>
/// There is one instance for the process, which is what <see cref="CreateCopy"/> answers: there is
/// one UI thread, and a second context would be a second answer to the same question. It resolves
/// the application at the moment of each call rather than recording the one that installed it,
/// because applications run one after another on that same thread.
/// </para>
/// </remarks>
internal sealed class UiSynchronizationContext : SynchronizationContext
{
    /// <summary>The one context this process has.</summary>
    internal static UiSynchronizationContext Instance { get; } = new();

    private UiSynchronizationContext()
    {
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Queued even when the caller is the UI thread, so a continuation never runs ahead of work that
    /// is already queued - a <c>Post</c> that ran the callback inline would let half a suspended
    /// method observe a tree the loop has not finished updating.
    /// </para>
    /// <para>
    /// Nothing is queued when there is no application to queue it on. A continuation can arrive
    /// after the loop has ended - the task it belonged to may have completed during teardown, and a
    /// message pump that is gone cannot be waited for - so the work is dropped rather than thrown
    /// about. That is the same outcome as the platform's own queue, which is discarded at shutdown.
    /// </para>
    /// </remarks>
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        Application.Current?.BeginInvoke(() => d(state));
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// There is no application, or its loop is not running, so waiting for the UI thread would never
    /// return.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Inline when the caller is already on the UI thread, which is the only way a <c>Send</c> can
    /// nest - a dispatch that queued and waited from the UI thread would deadlock behind itself.
    /// "The UI thread" is <see cref="Application.CheckAccess"/>, the same thread id every other
    /// member of the model uses, not a second notion of its own.
    /// </para>
    /// <para>
    /// Otherwise it waits for the loop, which it refuses to do when there is no loop:
    /// <c>Send</c> either runs the callback or throws. Waiting for a drain that has already finished
    /// hangs forever, and running the callback on the calling thread would be the cross-thread
    /// violation this type exists to prevent, at the one moment it would be hardest to diagnose.
    /// </para>
    /// </remarks>
    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);

        if (Application.Current is not { } application)
        {
            throw new InvalidOperationException(
                "There is no Application to send to. A SynchronizationContext.Send after Run() has " +
                "returned has no loop left to run its callback on.");
        }

        if (application.CheckAccess())
        {
            d(state);
            return;
        }

        if (!application.IsRunning)
        {
            throw new InvalidOperationException(
                "Application.Run() has not started, or the loop has already ended, so waiting for the " +
                "UI thread would never return. Send is only for a running application.");
        }

        application.Invoke(() => d(state));
    }

    /// <inheritdoc />
    /// <remarks>
    /// The same instance, not a copy: one UI thread, one context.
    /// </remarks>
    public override SynchronizationContext CreateCopy() => this;
}

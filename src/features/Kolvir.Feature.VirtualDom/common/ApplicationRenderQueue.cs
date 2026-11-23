namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// The application mounts that have asked to be rendered, drained once per frame.
/// </summary>
/// <remarks>
/// <para>
/// The application-level counterpart of <see cref="RenderQueue"/>. An
/// <see cref="ApplicationMount.Invalidate"/> only marks its mount and asks for a frame; the renders
/// themselves happen at the start of the next frame any host draws - see
/// <see cref="UiHost.AddFrameStartingHandler"/> - so a model change that arrives five times before
/// the frame renders the window set once.
/// </para>
/// <para>
/// The queue is process-wide for the same reason the control queue is: a mount can exist before any
/// window holds it, and every host that draws drains the same queue. The subscription is never
/// removed, because an idle check is one delegate call and an integer compare against a frame that
/// costs far more.
/// </para>
/// </remarks>
internal static class ApplicationRenderQueue
{
    private static readonly List<ApplicationMount> Pending = [];
    private static bool _subscribed;
    private static bool _draining;

    /// <summary>Adds a mount to the next frame's batch.</summary>
    /// <param name="mount">The mount to render.</param>
    internal static void Enqueue(ApplicationMount mount)
    {
        Subscribe();
        Pending.Add(mount);
    }

    /// <summary>Drops a mount that was disposed before its turn came.</summary>
    /// <param name="mount">The mount to forget.</param>
    internal static void Forget(ApplicationMount mount) => Pending.Remove(mount);

    /// <summary>Renders everything queued, once each, in the order the state changed.</summary>
    /// <remarks>
    /// The batch is taken before any render runs, because a render can dispose a mount that is still
    /// waiting its turn. A disposed mount renders nothing, so the batch stays simple. A render that
    /// throws goes to <see cref="Application.HandleNativeBoundary"/> and the rest of the batch still
    /// runs, for the same reason <see cref="RenderQueue"/> does: the entries have already left the
    /// queue, so letting the exception out would strand every mount behind it.
    /// </remarks>
    internal static void Drain()
    {
        if (_draining || Pending.Count == 0)
        {
            return;
        }

        var batch = Pending.ToArray();
        Pending.Clear();
        _draining = true;

        int next = 0;

        try
        {
            for (; next < batch.Length; next++)
            {
                try
                {
                    batch[next].RenderQueued();
                }
                catch (Exception exception)
                {
                    Application.HandleNativeBoundary(exception);
                }
            }
        }
        finally
        {
            _draining = false;

            // If a boundary chose to rethrow, the mounts the loop never reached are still marked
            // pending, so they have to go back on the queue or the flag would keep them off it.
            for (int i = next + 1; i < batch.Length; i++)
            {
                Pending.Add(batch[i]);
            }
        }
    }

    private static void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        UiHost.AddFrameStartingHandler(OnFrameStarting);
    }

    private static void OnFrameStarting(UiHost host) => Drain();
}

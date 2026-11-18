namespace Kolvir.Observables;

/// <summary>
/// The collectors: one synchronous run of a body, its subscriptions, and the dependencies it read.
/// </summary>
/// <remarks>
/// <para>
/// Two forms, one for a body with no result and one for a body that returns something. Both run the body
/// immediately, inside a collection scope, and both subscribe to every source the body reads <em>as it
/// reads it</em> - so what comes back is live, not a report to be hooked afterwards.
/// </para>
/// <code>
/// // The component seam, in full: the render is subscribed to what it read by the time it returns, and
/// // the token for that is the run itself.
/// _render?.Dispose();
/// _render = Observable.CollectDependencies(() =&gt; Build(), _ =&gt; _mount?.Invalidate());
///
/// return _render.Value;   // throws into the frame boundary if the render failed, still subscribed
/// </code>
/// <para>
/// These exist because a render is not an expression. A render produces a tree it does not want compared,
/// so what it needs is "listen to what this run read" plus "give me the result, or the failure", and
/// expressing that through an expression would mean comparing node trees to decide nothing.
/// </para>
/// </remarks>
public static partial class Observable
{
    /// <summary>Runs <paramref name="body"/>, subscribes to what it read, and reports it.</summary>
    /// <param name="body">The work whose reads are wanted. It has no result, and it is run once.</param>
    /// <param name="onChanged">
    /// What to call when a source the body read changes. The argument is the source, so one handler can
    /// serve every dependency of the run.
    /// </param>
    /// <returns>
    /// The sources the body read, subscribed until the result is disposed, and
    /// <see cref="Collected.Fault"/> if it threw. Whatever it read before throwing is in the set - those
    /// are what its failure was computed from, and so what can make it go away.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> or <paramref name="onChanged"/> is null.</exception>
    /// <remarks>
    /// The subscription is taken during the body, not after it, so a source read on the first line is
    /// already being watched by the second. A change made from inside the body is therefore delivered
    /// like any other, and can reach <paramref name="onChanged"/> before the body has finished - the
    /// caller's handler is not protected from that, and a body that changes something it read re-enters
    /// itself until <see cref="MaxEventStackSize"/> stops the chain.
    /// </remarks>
    public static Collected CollectDependencies(Action body, Action<IObservableEventSource> onChanged)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(onChanged);

        var scope = new CollectionScope(onChanged);
        Exception? fault = null;

        // The scope is restored on the way out even when the body throws, so a failed run cannot leave
        // the collector pointing at a run that has already ended. Nested runs are isolated: each one
        // captures the one it interrupted and puts it back.
        using (ReadScope.Begin(scope))
        {
            try
            {
                body();
            }
            catch (Exception exception)
            {
                fault = exception;
            }
        }

        return new Collected(scope.Dependencies, scope.Tokens, fault);
    }

    /// <summary>Runs <paramref name="body"/>, subscribes to what it read, and reports what it returned.</summary>
    /// <typeparam name="T">What the body returns.</typeparam>
    /// <param name="body">The work whose reads and result are wanted. It is run once.</param>
    /// <param name="onChanged">
    /// What to call when a source the body read changes. The argument is the source, so one handler can
    /// serve every dependency of the run.
    /// </param>
    /// <returns>
    /// The sources the body read, the value it returned, and <see cref="Collected.Fault"/> if it threw.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> or <paramref name="onChanged"/> is null.</exception>
    /// <remarks>
    /// The form the component seam uses, because it needs both halves at once: the subscriptions are
    /// taken as the body reads, so <see cref="Collected{T}.Value"/> can be read afterwards - and throws
    /// the body's exception if there was one - with the listening already in place either way.
    /// </remarks>
    public static Collected<T> CollectDependencies<T>(Func<T> body, Action<IObservableEventSource> onChanged)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(onChanged);

        var scope = new CollectionScope(onChanged);
        var value = default(T);
        Exception? fault = null;

        using (ReadScope.Begin(scope))
        {
            try
            {
                value = body();
            }
            catch (Exception exception)
            {
                fault = exception;
            }
        }

        return new Collected<T>(scope.Dependencies, scope.Tokens, fault, value!);
    }
}

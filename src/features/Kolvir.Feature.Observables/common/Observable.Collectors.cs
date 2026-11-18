namespace Kolvir.Observables;

/// <summary>
/// The collectors: one synchronous run of a body, and the dependencies it read.
/// </summary>
/// <remarks>
/// <para>
/// Two forms, one for a body with no result and one for a body that returns something. Both run the body
/// immediately, inside a collection scope, and both report what it read whether or not it finished.
/// </para>
/// <code>
/// var run = Observable.CollectDependencies(() =&gt; _panel.Render());
/// foreach (var dep in run.Dependencies)
/// {
///     tokens.Add(dep.AddObservableEventRaisedHandler(_ =&gt; Invalidate()));
/// }
///
/// return run.Value;   // throws into whoever called this, with the set already hooked
/// </code>
/// <para>
/// These exist because a render is not an expression. A render produces a tree it does not want
/// compared, so what it needs is "the dependencies of this run" plus "call me when one of them changes",
/// and expressing that through an expression would mean comparing node trees to decide nothing.
/// </para>
/// </remarks>
public static partial class Observable
{
    /// <summary>Runs <paramref name="body"/> and reports the dependencies it read.</summary>
    /// <param name="body">The work whose reads are wanted. It has no result, and it is run once.</param>
    /// <returns>
    /// The sources the body read, and <see cref="Collected.Fault"/> if it threw. Whatever it read before
    /// throwing is in the set, because those are what its failure was computed from.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <remarks>
    /// Nothing is subscribed: this reports, and registering on the result is the caller's next step. The
    /// exception the body threw is returned rather than thrown, in
    /// <see cref="Collected.Fault"/>, and <see cref="Collected.Throw"/> rethrows it for a caller that
    /// wants that.
    /// </remarks>
    public static Collected CollectDependencies(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var scope = new CollectionScope();
        Exception? fault = null;

        // The scope is restored on the way out even when the body throws, so a failed run cannot leave
        // the collector pointing at a scope that has already ended. Nested runs are isolated: each one
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

        return new Collected(scope.Dependencies, fault);
    }

    /// <summary>Runs <paramref name="body"/> and reports the dependencies it read and what it returned.</summary>
    /// <typeparam name="T">What the body returns.</typeparam>
    /// <param name="body">The work whose reads and result are wanted. It is run once.</param>
    /// <returns>
    /// The sources the body read, the value it returned, and <see cref="Collected.Fault"/> if it threw.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <remarks>
    /// The form the component seam uses, because it needs both halves: hook
    /// <see cref="Collected.Dependencies"/>, then read <see cref="Collected{T}.Value"/>, which throws the
    /// body's exception if there was one - so a render that failed still leaves its component listening
    /// to what it read, and the failure lands in whatever boundary is above the frame.
    /// </remarks>
    public static Collected<T> CollectDependencies<T>(Func<T> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var scope = new CollectionScope();
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

        return new Collected<T>(scope.Dependencies, fault, value!);
    }
}

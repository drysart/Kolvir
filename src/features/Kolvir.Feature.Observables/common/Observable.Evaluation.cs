using System.Collections.Immutable;

namespace Kolvir.Observables;

/// <summary>
/// The one dispatch, and the evaluation depth it shares with a cold read.
/// </summary>
/// <remarks>
/// <para>
/// Two things nest without bound, and they are the same recursion: a raise, because a handler may write
/// and the write raises, and a cold read, because a cold expression's function runs - and may read
/// another expression, which runs in turn. Each step is one frame deeper than the one before it, so both
/// are counted in one depth and a chain that alternates between them is bounded by the same number
/// (dev-docs/OBSERVABLES.md section 3.8).
/// </para>
/// <para>
/// Every notification in the package also goes through <see cref="RaiseToHandlers"/>: a value's write and
/// a value's <c>Dispose</c>, an expression's own change and an expression's <c>Dispose</c>. One dispatch
/// point is what lets one guard cover all of them.
/// </para>
/// </remarks>
public static partial class Observable
{
    private static int _maxEventStackSize = 50;
    private static int _depth;
    private static bool _isOverflowed;

    /// <summary>
    /// How deep evaluation may nest - one event raise or one cold read per level - before the next step
    /// is refused. Fifty until it is changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A raise calls a handler, and a handler is free to write and raise again; a cold read runs a
    /// function, and a function is free to read another expression that runs in turn. Either way each
    /// step is a frame deeper. At <see cref="MaxEventStackSize"/> levels the next step is refused instead
    /// of being allowed to exhaust the stack, which under .NET kills the process with no report at all -
    /// and the refusal is sticky for the rest of the chain, so nothing that catches it can resume the
    /// descent.
    /// </para>
    /// <para>
    /// A guard, not a cycle detector. Legitimate evaluation nests: one change settling a handful of
    /// dependent expressions is a chain of three or four, and so is a graph of derived values read from
    /// the top while nothing is subscribed. Fifty sits far above any of that, and the property exists for
    /// the application whose graphs are deeper than that.
    /// </para>
    /// <para>
    /// Read on every raise and every cold read, so it stays a field rather than a lookup, and written on
    /// the UI thread like everything else here (section 3.9).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than one.</exception>
    public static int MaxEventStackSize
    {
        get => _maxEventStackSize;

        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _maxEventStackSize = value;
        }
    }

    /// <summary>Calls every handler in <paramref name="handlers"/> for <paramref name="source"/>.</summary>
    /// <param name="source">What raised, which is what each handler is given.</param>
    /// <param name="handlers">The handlers to call, as the source held them when the raise began.</param>
    /// <exception cref="InvalidOperationException">
    /// The raise would nest deeper than <see cref="MaxEventStackSize"/>, or the chain it is part of has
    /// already overrun and is still unwinding.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The array is a snapshot for free. Subscribing or unsubscribing replaces the source's field rather
    /// than editing the array, so changes apply to the next raise and a handler that disposes the source
    /// mid-dispatch does not shorten this one - the remaining handlers are already in the array being
    /// walked.
    /// </para>
    /// <para>
    /// Nothing is caught: the first handler that throws ends the notification and its exception travels
    /// out of the raise exactly as thrown. The depth is restored on the way out either way, and an
    /// overrun holds the whole chain refused until the outermost step unwinds, so a chain that failed at
    /// the limit leaves nothing behind.
    /// </para>
    /// </remarks>
    internal static void RaiseToHandlers(
        IObservableEventSource source,
        ImmutableArray<Action<IObservableEventSource>> handlers)
    {
        // Nothing to call and nothing to recurse into, so a raise with no subscribers does not count as
        // one - a dead value disposing is the common case and stays free.
        if (handlers.IsEmpty)
        {
            return;
        }

        using var evaluation = EnterRaise(source);

        foreach (var handler in handlers)
        {
            handler(source);
        }
    }

    /// <summary>Whether a raise or a cold read is in progress, at any depth.</summary>
    /// <remarks>
    /// What the named table waits for: eviction is deferred while something is running, because the code
    /// that has just emptied an entry is very often the code that is about to fill it again
    /// (see <see cref="NamedDependencies"/>).
    /// </remarks>
    internal static bool IsEvaluating => _depth > 0;

    /// <summary>
    /// Counts one level for a raise, for as long as the caller's scope runs.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The chain is already too deep, or this step makes it so.
    /// </exception>
    internal static EvaluationToken EnterRaise(IObservableEventSource source) =>
        Enter(source, "An event raise");

    /// <summary>
    /// Counts one level for a run started by reading an expression with nothing subscribed, for as long as
    /// the caller's scope runs.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The chain is already too deep, or this step makes it so.
    /// </exception>
    internal static EvaluationToken EnterColdRead(IObservableEventSource expression) =>
        Enter(expression, "A cold read");

    private static EvaluationToken Enter(IObservableEventSource source, string step)
    {
        // Once a chain has overrun, everything still unwinding through it is on a stack that has already
        // been declared too deep. A handler that catches the refusal - or a collector that turns a throwing
        // run into a cached failure, which is what a run does with any exception - must not be able to
        // start the descent again from where it is holding on.
        if (_isOverflowed)
        {
            throw Overflowed(source, step);
        }

        var previousDepth = _depth;
        var depth = previousDepth + 1;

        if (depth > _maxEventStackSize)
        {
            _isOverflowed = true;
            throw Overflowed(source, step);
        }

        _depth = depth;

        return new EvaluationToken(previousDepth);
    }

    private static InvalidOperationException Overflowed(IObservableEventSource source, string step) =>
        new($"{step} nested past Observable.MaxEventStackSize ({_maxEventStackSize}), at {source}. Every "
            + "step of a chain like this runs inside the one before it, so a chain that keeps growing is "
            + "a loop: a handler that writes something the chain depends on, or an expression whose "
            + "function reads itself before anything is subscribed to it. The rest of the chain is "
            + "abandoned rather than evaluated - raise MaxEventStackSize if that depth is legitimate, and "
            + "break the loop if it is not.");

    /// <summary>Puts the depth back, and ends an overrun when the outermost step is the one returning.</summary>
    /// <remarks>
    /// A struct and not a class, so entering a step allocates nothing at all: this runs on every raise and
    /// on every cold read.
    /// </remarks>
    internal readonly struct EvaluationToken : IDisposable
    {
        private readonly int _previousDepth;

        internal EvaluationToken(int previousDepth) => _previousDepth = previousDepth;

        /// <inheritdoc/>
        public void Dispose()
        {
            _depth = _previousDepth;

            // The outermost step ending is what ends the refusal, so an overrun cannot poison the next
            // change: by the time the call that started it returns, the depth is zero and nothing is left.
            if (_previousDepth == 0)
            {
                _isOverflowed = false;

                // And it is the first moment the named table may evict anything: an entry emptied by this
                // chain might have been re-registered by the very run that emptied it (section 3.2).
                NamedDependencies.Drain();
            }
        }
    }
}

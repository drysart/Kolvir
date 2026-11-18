using System.Collections.Immutable;

namespace Kolvir.Observables;

/// <summary>
/// The raise itself, and the one limit on how far a chain of them may nest.
/// </summary>
/// <remarks>
/// <para>
/// Every notification in the package goes through <see cref="RaiseToHandlers"/>: a value's write and a
/// value's <c>Dispose</c>, an expression's own change and an expression's <c>Dispose</c>. One dispatch
/// point is what makes one depth counter cover all of them.
/// </para>
/// <para>
/// The depth is the answer to a loop the graph cannot describe. A handler may write, a write raises, and
/// the raise may reach the handler again - so an endless chain is not an exotic state, it is one wrong
/// write away in any application that has a cycle in its handler graph. Counting raises catches that
/// wherever it happens, without knowing anything about which values or expressions the chain runs
/// through (dev-docs/OBSERVABLES.md section 3.8).
/// </para>
/// </remarks>
public static partial class Observable
{
    private static int _maxEventStackSize = 50;
    private static int _eventDepth;
    private static bool _isOverflowed;

    /// <summary>
    /// How many event raises may be in flight at any one time. Fifty until it is changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raising calls a handler, and a handler is free to write and raise again, so each step of a chain
    /// is a frame deeper. At <see cref="MaxEventStackSize"/> levels of nesting the next raise is refused
    /// instead of being allowed to exhaust the stack, which under .NET kills the process with no report
    /// at all - and the refusal is sticky for the rest of the chain, so nothing that catches it can
    /// resume the descent.
    /// </para>
    /// <para>
    /// A guard, not a cycle detector. Legitimate changes nest - one change settling a handful of
    /// dependent expressions - so the limit sits far above any real depth and an application with a
    /// deeper chain it actually wants raises it. A loop that never raises is not caught at all: an
    /// expression that reads itself while nothing is subscribing recurses in plain function calls, with
    /// no event anywhere to count, and the depth cannot see it.
    /// </para>
    /// <para>
    /// Read on every raise, so it stays a field rather than a lookup, and written on the UI thread like
    /// everything else here (section 3.9).
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
    /// overrun holds the whole chain refused until the outermost raise unwinds, so a chain that failed at
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

        // Once a chain has overrun, everything still unwinding through it is on a stack that has already
        // been declared too deep. A handler that catches the refusal - or a collector that turns a throwing
        // run into a cached failure, which is what a run does with any exception - must not be able to
        // start the descent again from where it is holding on.
        if (_isOverflowed)
        {
            throw Overflowed(source);
        }

        var previousDepth = _eventDepth;
        var depth = previousDepth + 1;

        if (depth > _maxEventStackSize)
        {
            _isOverflowed = true;
            throw Overflowed(source);
        }

        _eventDepth = depth;

        try
        {
            foreach (var handler in handlers)
            {
                handler(source);
            }
        }
        finally
        {
            _eventDepth = previousDepth;

            // The outermost raise ending is what ends the refusal, so an overrun cannot poison the next
            // change: by the time the call that started it returns, the depth is zero and nothing is left.
            if (previousDepth == 0)
            {
                _isOverflowed = false;
            }
        }
    }

    private static InvalidOperationException Overflowed(IObservableEventSource source) =>
        new($"An event raise nested past Observable.MaxEventStackSize ({_maxEventStackSize}), raising "
            + $"for {source}. Every handler in a chain like this runs inside the one before it, so a chain "
            + "that keeps growing is a loop - most often a handler that writes something the chain "
            + "depends on. The rest of the chain is abandoned rather than evaluated: raise "
            + "MaxEventStackSize if that depth is legitimate, and break the loop if it is not.");
}

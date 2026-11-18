namespace Kolvir.Observables;

/// <summary>
/// The named dependencies: one key, a handle that names it, and a way to raise a key nobody has a handle
/// for.
/// </summary>
/// <remarks>
/// <para>
/// A named dependency is how something that is not a value says it changed - a row in a dictionary, a key
/// in a collection, a property of a model that is keyed rather than stored. It carries no value, so
/// whatever is being announced stays where it lives (dev-docs/OBSERVABLES.md section 3.2).
/// </para>
/// <code>
/// // A collection announcing that one row changed. No handle, no registration, no allocation.
/// Observable.RaiseNamedEvent(row.Key);
///
/// // An expression that wants to hear about that row: depend on the key, then read the row.
/// var text = Observable.CreateExpression(() =&gt;
/// {
///     Observable.GetNamed(row.Key).DependUpon();
///     return model.TextOf(row.Key);
/// });
/// </code>
/// <para>
/// Keys are opaque: compose a multi-part name into one object - a tuple, a small record - and the layer
/// passes it through untouched. They compare by the type's own equality, so a tuple key compares
/// structurally.
/// </para>
/// </remarks>
public static partial class Observable
{
    /// <summary>A handle naming <paramref name="key"/>.</summary>
    /// <param name="key">What the dependency is named. Compared by the key's own equality.</param>
    /// <returns>
    /// The handle to depend on. Handles for equal keys compare equal, so it does not matter which one a run
    /// gets: reading the same key twice in one run is one dependency, and a handler registered through any
    /// handle hears a raise made through any other.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// A name and nothing else, which is why a caller may keep one. It holds no handlers and no value, so it
    /// cannot go stale, and holding it across an expression's re-collection - or as a model's field for
    /// raising - is ordinary rather than a mistake. What holds handlers is the table, and it holds them for
    /// exactly as long as something is subscribed.
    /// </para>
    /// <para>
    /// It costs one small allocation and nothing else: asking for a handle does not create an entry, because
    /// nothing is watching the key yet. Announcing a change does not need a handle either -
    /// <see cref="RaiseNamedEvent"/> resolves the key on its own, so an unwatched key costs a lookup.
    /// </para>
    /// <para>
    /// Value-type keys are boxed for the table's lookup, so a run that reads a keyed dependency repeatedly
    /// should hold the handle rather than call this per read (section 11).
    /// </para>
    /// </remarks>
    public static IObservableEventSource GetNamed(object key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return new NamedEventSource(key);
    }

    /// <summary>Raises the named dependency for <paramref name="key"/>, if anyone is watching it.</summary>
    /// <param name="key">The key whose thing changed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    /// <remarks>
    /// The shape a model wants: it owns the thing that changed and does not want to register on it, so this
    /// resolves the entry at the moment of the raise and does nothing at all when there is none - which
    /// makes an unwatched key one dictionary lookup, with no entry to create and nothing to walk.
    /// <para>
    /// It is a raise like any other, so it dispatches through the same depth-counted seam
    /// (section 3.8): a key raised from inside a handler that it wakes is a loop like any other.
    /// </para>
    /// </remarks>
    public static void RaiseNamedEvent(object key)
    {
        ArgumentNullException.ThrowIfNull(key);

        NamedDependencies.Raise(key);
    }
}

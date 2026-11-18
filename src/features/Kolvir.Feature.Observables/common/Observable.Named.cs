namespace Kolvir.Observables;

/// <summary>
/// The named dependencies: one key, one handle, and a way to raise a key nobody has a handle for.
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
    /// <summary>The handle for <paramref name="key"/>, created if there is none.</summary>
    /// <param name="key">What the dependency is named. Compared by the key's own equality.</param>
    /// <returns>
    /// The one handle for that key, which is the same instance for every caller and for as long as the key
    /// is watched. It is an <see cref="IObservableEventSource"/> and nothing more: depend on it inside a run,
    /// and raise it - or <see cref="RaiseNamedEvent"/> it - when the thing behind the key changes.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Resolve the handle where it is used rather than holding it: the entry exists only while something is
    /// registered on it, so the handle a run keeps alive is the one it registered on. A handle kept past
    /// that - across a re-collection, or as a field for raising - is dead, and says so by throwing
    /// (section 3.10).
    /// </para>
    /// <para>
    /// Asking for a handle creates the entry, because the caller is about to register on it. Announcing a
    /// change does not need a handle, and <see cref="RaiseNamedEvent"/> exists so that announcing one for
    /// an unwatched key costs a lookup and nothing else.
    /// </para>
    /// <para>
    /// Value-type keys are boxed for the lookup, so a caller that reads a keyed dependency on a hot path
    /// should hold the handle for the length of the run that reads it (section 11).
    /// </para>
    /// </remarks>
    public static IObservableEventSource GetNamed(object key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return NamedDependencies.Get(key);
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

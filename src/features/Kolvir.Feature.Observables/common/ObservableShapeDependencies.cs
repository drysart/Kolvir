namespace Kolvir.Feature.Observables;

/// <summary>
/// The two sources every collection has: how many items it holds, and what they are.
/// </summary>
/// <remarks>
/// <para>
/// The shape and the contents are the questions a read can ask about the whole collection rather than
/// about one address in it, and they are the same for every collection, so they live here and not in one
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 5). Both are created with the collection and never
/// dropped, which is why neither needs an entry table.
/// </para>
/// <para>
/// Marking is separate from raising for the reason the collections document gives: a delivery marks what
/// its records invalidated with no handler running, and only then raises, so one event raises each entry
/// once however many records touched it.
/// </para>
/// </remarks>
internal sealed class ObservableShapeDependencies
{
    private readonly ObservableDependency _count;
    private readonly ObservableDependency _contents;

    private bool _countMarked;
    private bool _contentsMarked;

    /// <summary>Creates the shape and contents sources for one collection.</summary>
    /// <param name="collection">What the collection is called in a diagnostic, such as
    /// <c>"an observable list"</c>.</param>
    internal ObservableShapeDependencies(string collection)
    {
        // Two of these per collection and made once with it, so keeping the text is the cheap choice
        // here - there is one name per source rather than one per watched address.
        _count = new ObservableDependency($"the count of {collection}");
        _contents = new ObservableDependency($"the contents of {collection}");
    }

    /// <summary>Publishes a read of the size.</summary>
    internal void PublishCount() => Observable.PublishRead(_count);

    /// <summary>Publishes a read that looked at every item.</summary>
    internal void PublishContents() => Observable.PublishRead(_contents);

    /// <summary>Marks both answers, for a change that added or removed an item.</summary>
    internal void MarkShape()
    {
        _countMarked = true;
        _contentsMarked = true;
    }

    /// <summary>Marks only the contents, for a change that moved an item without adding or removing
    /// one.</summary>
    internal void MarkContents() => _contentsMarked = true;

    /// <summary>Raises everything marked, once each, and clears the marks.</summary>
    /// <remarks>
    /// Handlers run here and are free to read the collection, which publishes dependencies of its own -
    /// those are new subscriptions and are not raised for the change that woke them, exactly as a value
    /// that starts being read in a handler is not re-raised for the write that caused it.
    /// </remarks>
    internal void Raise()
    {
        try
        {
            if (_countMarked)
            {
                _count.RaiseEvent();
            }

            if (_contentsMarked)
            {
                _contents.RaiseEvent();
            }
        }
        finally
        {
            // The flags are what makes one delivery raise an entry once however many of its records
            // touched it, so they are per delivery and have to be cleared with them - a flag left set
            // would silence that entry for every delivery after this one.
            _countMarked = false;
            _contentsMarked = false;
        }
    }

    /// <summary>Drops every subscription, for a collection that is being disposed.</summary>
    /// <remarks>
    /// Silent rather than raised: the event is the collection's liveness signal, and a dependency of a
    /// dead collection has nothing useful to say about it. What this is for is the memory - the closures
    /// registered through these sources are dropped with them.
    /// </remarks>
    internal void Release()
    {
        _count.Clear();
        _contents.Clear();
        _countMarked = false;
        _contentsMarked = false;
    }
}

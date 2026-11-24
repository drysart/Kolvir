using System.Collections;

namespace Kolvir.Feature.Observables;

/// <summary>
/// A read-only view over another observable dictionary, holding one entry per source key with its
/// value projected through <typeparamref name="TOuter"/>'s mapping function.
/// </summary>
/// <typeparam name="TKey">The type of the keys, which is the identity here as it is in the source.</typeparam>
/// <typeparam name="TInner">The type of the values of the dictionary being watched.</typeparam>
/// <typeparam name="TOuter">The type of the values this view holds.</typeparam>
/// <remarks>
/// <para>
/// The dictionary's counterpart of the mapped list view
/// (<see cref="ObservableMappedReadOnlyListView{TInner, TOuter}"/>) and the mapped set view
/// (<see cref="ObservableMappedReadOnlySetView{TInner, TOuter}"/>), and - like the list's and unlike
/// the set's - a record-for-record translation. A key is unique, so every entry's value has exactly one
/// mapped counterpart: nothing collapses, nothing merges, and no entry can arrive already present under
/// another identity. The set has to count; this does not
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 7.5).
/// </para>
/// <para>
/// <b>There is no <typeparamref name="TOuter"/> comparer.</b> The key is the identity and the value is
/// not, so a dictionary may hold the same value under two keys and that is a normal dictionary. The
/// view is therefore built with <c>_inner.Comparer</c> and <see cref="Comparer"/> answers that same key
/// comparer - what decides which entries are the same entry, exactly as
/// <see cref="IReadOnlyObservableDictionary{TKey, TValue}"/> requires. <typeparamref name="TOuter"/> is
/// unconstrained for the same reason: a value may be null.
/// </para>
/// <para>
/// <b>The mapping is eager and cached.</b> A <see cref="Dictionary{TKey, TValue}"/> keyed by the source
/// key holds the mapped value, and the function runs once per entry that arrives: on an add and on a
/// replacement's new value, never on a removal, never on a clear, and once per source entry on a
/// resync. A removal reports the value the view already held, because the record names the key and this
/// view is keyed by the key - there is nothing to map again. Reading this view therefore costs a
/// dictionary read, which is the reason it exists rather than a projection on every read.
/// </para>
/// <para>
/// <b>Mutating a value in place is not observed.</b> If <typeparamref name="TInner"/> is mutable and
/// changes internally, this view does not follow it: the source dictionary reports entries arriving,
/// leaving and being replaced, not changes inside a value, and nothing here can see what it is not told.
/// Writing the key again (<c>dictionary[k] = ...</c>) re-maps it, and so does any event that forces a
/// resync.
/// </para>
/// <para>
/// <b>Reads publish dependencies, one kind per question</b>, exactly as the dictionary does
/// (<see cref="IReadOnlyObservableDictionary{TKey, TValue}"/>): <see cref="IReadOnlyCollection{T}.Count"/>
/// and <see cref="Keys"/> publish the structure, <c>this[key]</c>, <see cref="ContainsKey"/> and
/// <see cref="TryGetValue"/> publish that key, and <see cref="Values"/>, enumeration,
/// <see cref="Contains"/> and <see cref="CopyTo"/> publish the contents. An expression over this view is
/// therefore woken through it and not through the source, and a reader of one key is not woken by a
/// change to another. The indexer publishes only when the key is there, and throws
/// <see cref="KeyNotFoundException"/> otherwise, because a key that is gone is not an address to depend
/// on (section 7.4).
/// </para>
/// <para>
/// <b>A record that does not fit the cache heals it.</b> If a change or a removal names a key the view
/// does not hold, or an add names one it already holds, the storage and the source disagree. The view
/// answers by rebuilding from the source and delivering one
/// <see cref="ObservableDictionaryChangeType.UntrackedChanges"/>, rather than guessing from the
/// record's fields, so the disagreement is visible and the view heals. That is reachable: a selector
/// that throws leaves the view behind the source, because the source applies and records its change
/// first and the exception then escapes the view's translation, which is where the view's own
/// application of it would have happened. The next record naming that key finds no entry, resyncs and
/// admits that the change was not described. No test is required for the add-shaped branch, which the
/// source's own contract makes unreachable; it is there so a broken caller cannot crash a delivery.
/// </para>
/// <para>
/// <b>The source is not owned.</b> The view subscribes to it and reads it; it never disposes it, because
/// the same source may be read directly and by other views. <see cref="IDisposable.Dispose"/> ends this
/// view's life only: it unsubscribes, drops what had not been delivered, and delivers one last event
/// with no records, as <see cref="IReadOnlyObservableDictionary{TKey, TValue}"/> specifies. A source
/// that is disposed first delivers its own final event, which this view ignores - there can never be
/// another change, so the view keeps the contents it last had and stays readable.
/// </para>
/// <para>
/// <b>The mapping function is called from the source's notification.</b> It runs on the thread that
/// changed the source, inside the source dictionary's delivery, at most once per value that arrives or
/// changes; it must not throw and must not change the source dictionary or this view. A function that
/// throws propagates out of the source dictionary's delivery, like any other handler's exception, and
/// leaves this view behind the source until a record naming that key heals it.
/// </para>
/// <para>
/// Single-threaded, like every collection in this package: no locks, no cross-thread delivery. It is not
/// an <see cref="IObservableEventSource"/> either, for the reason
/// <see cref="IReadOnlyObservableDictionary{TKey, TValue}"/> gives about dictionaries.
/// </para>
/// </remarks>
public sealed class ObservableMappedReadOnlyDictionaryView<TKey, TInner, TOuter>
    : IReadOnlyObservableDictionary<TKey, TOuter>,
      IObservableChangeTarget<ObservableDictionaryChange<TKey, TOuter>, ObservableDictionaryChangedEventArgs<TKey, TOuter>>
    where TKey : notnull
{
    private readonly IReadOnlyObservableDictionary<TKey, TInner> _inner;
    private readonly Func<TInner, TOuter> _selector;
    private readonly Action<ObservableDictionaryChangedEventArgs<TKey, TInner>> _onInnerChanged;
    private readonly Dictionary<TKey, TOuter> _items;
    private readonly ObservableDictionaryDependencies<TKey, TOuter> _dependencies;
    private readonly ObservableChangeBatch<ObservableDictionaryChange<TKey, TOuter>, ObservableDictionaryChangedEventArgs<TKey, TOuter>> _batch;

    /// <summary>Creates a view over <paramref name="inner"/>, projecting each value with
    /// <paramref name="selector"/>.</summary>
    /// <param name="inner">The dictionary to watch. The view subscribes to it and does not own it.</param>
    /// <param name="selector">What turns a source value into the value this view holds. Called once per
    /// value that arrives or changes, and once per value the source already held when this was
    /// created.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> or
    /// <paramref name="selector"/> is null.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="inner"/> is already disposed.</exception>
    /// <remarks>
    /// The contents the source already holds are mapped here, once each; the subscription is taken
    /// afterwards, which is what keeps an event from arriving before there is a dictionary to apply it
    /// to. The cache is keyed with the source's own comparer, so which entries are the same entry is the
    /// source's answer and not a second rule invented here. Nothing is raised: there is nobody
    /// registered yet, and the mapped entries are the starting state rather than a change to it.
    /// </remarks>
    public ObservableMappedReadOnlyDictionaryView(
        IReadOnlyObservableDictionary<TKey, TInner> inner,
        Func<TInner, TOuter> selector)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(selector);

        _inner = inner;
        _selector = selector;
        _onInnerChanged = OnInnerChanged;

        _items = new Dictionary<TKey, TOuter>(inner.Comparer);

        // The cache's own comparer, which is the source's - the dependencies have to key by the same rule.
        _dependencies = new ObservableDictionaryDependencies<TKey, TOuter>(_items.Comparer);
        _batch = new(this);

        foreach (var item in inner)
        {
            _items.Add(item.Key, selector(item.Value));
        }

        inner.AddObservableDictionaryChangedHandler(_onInnerChanged);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A constant of the view and not a read of anything, so it publishes no dependency and stays
    /// readable once the view is dead.
    /// </remarks>
    public bool IsDisposed => _batch.IsDisposed;

    /// <inheritdoc/>
    /// <remarks>
    /// A constant of the view and not a read of anything, so it publishes no dependency and stays
    /// readable once the view is dead. It is the source's key comparer, because the key is the identity
    /// here and the value is not - there is no outer comparer, and two keys may hold equal values.
    /// </remarks>
    public IEqualityComparer<TKey> Comparer => _inner.Comparer;

    /// <inheritdoc/>
    /// <remarks>Publishes the structure dependency: raised by a key arriving, a key leaving, or a clear,
    /// and never by a value changing beside a key that stayed.</remarks>
    public int Count
    {
        get
        {
            ThrowIfDisposed();

            _dependencies.PublishCount();
            return _items.Count;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Publishes the structure dependency, because a key's identity cannot change without it being a
    /// different key. It is the storage's own live view, so enumerating it during a change throws, as
    /// every other key collection in the BCL does.
    /// </remarks>
    public IEnumerable<TKey> Keys
    {
        get
        {
            ThrowIfDisposed();

            _dependencies.PublishCount();
            return _items.Keys;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Publishes the contents dependency, because a value changes every time somebody edits one. It is
    /// the storage's own live view, with the same enumeration behaviour <see cref="Keys"/> describes.
    /// </remarks>
    public IEnumerable<TOuter> Values
    {
        get
        {
            ThrowIfDisposed();

            _dependencies.PublishContents();
            return _items.Values;
        }
    }

    /// <summary>The mapped value beside <paramref name="key"/>.</summary>
    /// <param name="key">Which entry.</param>
    /// <value>The value the source's value for that key mapped to when it arrived or last changed.</value>
    /// <exception cref="KeyNotFoundException">The key is not in the view.</exception>
    /// <exception cref="ObjectDisposedException">The view is disposed.</exception>
    /// <remarks>
    /// Reading publishes the key's dependency, and only when the key is there: a key that is gone is the
    /// wrong shape of question to remember, and <see cref="TryGetValue"/> and <see cref="ContainsKey"/>
    /// are the shapes that survive it, exactly as the source dictionary answers it (section 7.4).
    /// </remarks>
    public TOuter this[TKey key]
    {
        get
        {
            ThrowIfDisposed();

            if (!_items.TryGetValue(key, out var value))
            {
                throw new KeyNotFoundException(
                    $"The key {key} is not in the view. A read of an absent key publishes nothing, "
                    + "because a key that is gone is not an address to depend on - ask with TryGetValue or "
                    + "ContainsKey to be woken by it appearing.");
            }

            _dependencies.PublishKey(key);
            return value;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Publishes the key's dependency before answering, whether or not the key is there, so a
    /// reader that asked about an absent key is woken by the key appearing.</remarks>
    public bool ContainsKey(TKey key)
    {
        ThrowIfDisposed();

        _dependencies.PublishKey(key);
        return _items.ContainsKey(key);
    }

    /// <inheritdoc/>
    /// <remarks>Publishes the key's dependency before answering, whether or not the key is there.</remarks>
    public bool TryGetValue(TKey key, out TOuter value)
    {
        ThrowIfDisposed();

        _dependencies.PublishKey(key);
        return _items.TryGetValue(key, out value!);
    }

    /// <summary>Whether the view holds <paramref name="item"/> - that key, with that mapped value.</summary>
    /// <param name="item">The key, and the value it must have.</param>
    /// <returns>True when the key is there with that value.</returns>
    /// <exception cref="ObjectDisposedException">The view is disposed.</exception>
    /// <remarks>
    /// Not on <see cref="IReadOnlyDictionary{TKey, TValue}"/>, kept as the counterpart of the other two
    /// views' scans and of the dictionary's own <c>Contains(KeyValuePair)</c>.
    /// Publishes the contents dependency, because it asks about a value as well as a key, and delegates
    /// to the storage so the BCL's equality rules are the ones that answer.
    /// </remarks>
    public bool Contains(KeyValuePair<TKey, TOuter> item)
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return ((ICollection<KeyValuePair<TKey, TOuter>>)_items).Contains(item);
    }

    /// <summary>Copies every entry into <paramref name="array"/>, starting at
    /// <paramref name="arrayIndex"/>.</summary>
    /// <param name="array">Where to copy them.</param>
    /// <param name="arrayIndex">The first position to write.</param>
    /// <exception cref="ObjectDisposedException">The view is disposed.</exception>
    /// <remarks>
    /// Not on <see cref="IReadOnlyDictionary{TKey, TValue}"/>, for the reason <see cref="Contains"/>
    /// gives. Publishes the contents dependency, because it reads every entry, and delegates to the
    /// storage for its argument checks.
    /// </remarks>
    public void CopyTo(KeyValuePair<TKey, TOuter>[] array, int arrayIndex)
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        ((ICollection<KeyValuePair<TKey, TOuter>>)_items).CopyTo(array, arrayIndex);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Publishes the contents dependency, because an enumeration answers from every value. The enumerator
    /// is the storage's own, so mutating during the enumeration throws
    /// <see cref="InvalidOperationException"/>.
    /// </remarks>
    public IEnumerator<KeyValuePair<TKey, TOuter>> GetEnumerator()
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return _items.GetEnumerator();
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    public IDisposable AddObservableDictionaryChangedHandler(
        Action<ObservableDictionaryChangedEventArgs<TKey, TOuter>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return _batch.AddHandler(handler);
    }

    /// <inheritdoc/>
    public void RemoveObservableDictionaryChangedHandler(
        Action<ObservableDictionaryChangedEventArgs<TKey, TOuter>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _batch.RemoveHandler(handler);
    }

    /// <summary>
    /// Ends the view's life: unsubscribes from the source, drops any change that had not been delivered,
    /// delivers one last event with no changes, and lets every handler go.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source dictionary is not disposed and stays usable - the view does not own it, may be one of
    /// several readers of it, and a teardown that took the model's dictionary down with the screen would
    /// be a bug. What ends here is this view's subscription to it.
    /// </para>
    /// <para>
    /// Idempotent, and safe after the source is gone, because the removal is tolerant by
    /// <see cref="IReadOnlyObservableDictionary{TKey, TValue}"/>'s contract.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_batch.IsDisposed)
        {
            return;
        }

        try
        {
            // Before the final event, so nothing the source is delivering can reach this view afterwards.
            _inner.RemoveObservableDictionaryChangedHandler(_onInnerChanged);
        }
        finally
        {
            _batch.Dispose();
        }
    }

    /// <summary>What this view is, for a diagnostic.</summary>
    public override string ToString() =>
        $"an observable view of {typeof(TKey).Name} to {typeof(TInner).Name} as {typeof(TOuter).Name}";

    /// <inheritdoc/>
    int IObservableChangeTarget<ObservableDictionaryChange<TKey, TOuter>, ObservableDictionaryChangedEventArgs<TKey, TOuter>>.Size =>
        _items.Count;

    /// <inheritdoc/>
    ObservableDictionaryChange<TKey, TOuter>
        IObservableChangeTarget<ObservableDictionaryChange<TKey, TOuter>, ObservableDictionaryChangedEventArgs<TKey, TOuter>>.Untracked =>
        ObservableDictionaryChange<TKey, TOuter>.Untracked();

    /// <inheritdoc/>
    bool IObservableChangeTarget<ObservableDictionaryChange<TKey, TOuter>, ObservableDictionaryChangedEventArgs<TKey, TOuter>>.IsUntracked(
        in ObservableDictionaryChange<TKey, TOuter> change) =>
        change.ChangeType == ObservableDictionaryChangeType.UntrackedChanges;

    /// <inheritdoc/>
    ObservableDictionaryChangedEventArgs<TKey, TOuter>
        IObservableChangeTarget<ObservableDictionaryChange<TKey, TOuter>, ObservableDictionaryChangedEventArgs<TKey, TOuter>>.CreateArgs(
            IReadOnlyList<ObservableDictionaryChange<TKey, TOuter>> changes) =>
        new(this, changes);

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableDictionaryChange<TKey, TOuter>, ObservableDictionaryChangedEventArgs<TKey, TOuter>>.Mark(
        IReadOnlyList<ObservableDictionaryChange<TKey, TOuter>> changes,
        int size) =>
        _dependencies.Mark(changes);

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableDictionaryChange<TKey, TOuter>, ObservableDictionaryChangedEventArgs<TKey, TOuter>>.RaiseDependencies() =>
        _dependencies.Raise();

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableDictionaryChange<TKey, TOuter>, ObservableDictionaryChangedEventArgs<TKey, TOuter>>.ReleaseDependencies() =>
        _dependencies.Release();

    /// <summary>Translates what the source delivered into the mapped record stream, and delivers it once.</summary>
    /// <remarks>
    /// <para>
    /// Every record is applied to the cache before it is recorded, because a handler that reads the view
    /// must already see what the records it is being handed describe - the same order the source
    /// dictionary itself mutates in.
    /// </para>
    /// <para>
    /// The loop runs inside one batch of this view's own. The source's delivery is already the unit an
    /// observer sees, so a batch of three source changes has to leave here as one event with three
    /// translated records rather than three events. That batch carries the package default cap and knows
    /// nothing about a source whose owner raised its own: an event longer than the default collapses here
    /// to one untracked record, while the cache is already complete, which is the same
    /// fidelity-for-boundedness trade a single dictionary makes, one level down.
    /// </para>
    /// </remarks>
    private void OnInnerChanged(ObservableDictionaryChangedEventArgs<TKey, TInner> args)
    {
        if (_batch.IsDisposed)
        {
            // The source hands its handlers out as a snapshot, so one taken before our unsubscribe can
            // still call us. Nothing may touch the cache after the view is dead.
            return;
        }

        var changes = args.Changes;

        if (changes.Count == 0)
        {
            // The source's one empty event is its disposal: it can never change again. The view keeps the
            // contents it last had and stays readable - the source's death is not this view's death.
            return;
        }

        _batch.BeginChangeBatch();

        try
        {
            for (var index = 0; index < changes.Count; index++)
            {
                Translate(changes[index]);
            }
        }
        finally
        {
            // Delivers the records that were applied, even when the selector threw part way: what is
            // pending always describes what the cache now holds.
            _batch.EndChangeBatch();
        }
    }

    private void Translate(in ObservableDictionaryChange<TKey, TInner> change)
    {
        switch (change.ChangeType)
        {
            case ObservableDictionaryChangeType.ItemAdded:
            {
                var key = change.Key!;

                if (_items.ContainsKey(key))
                {
                    // Unreachable while the source keeps its own contract - it throws before it records a
                    // second add for one key - and it is here anyway, because a record that does not fit
                    // the cache heals it instead of escaping the delivery as an argument exception.
                    Resync();
                    break;
                }

                var value = _selector(change.NewValue!);

                _items.Add(key, value);
                _batch.Record(ObservableDictionaryChange<TKey, TOuter>.Add(key, value));
                break;
            }

            case ObservableDictionaryChangeType.ItemChanged:
            {
                var key = change.Key!;

                if (!_items.TryGetValue(key, out var oldValue))
                {
                    // The cache and the source disagree about which keys it holds, which is a view behind
                    // the source - a selector that threw is the reachable cause. Rebuild and admit it
                    // rather than throwing or inventing an old value from the record.
                    Resync();
                    break;
                }

                var value = _selector(change.NewValue!);

                _items[key] = value;
                _batch.Record(ObservableDictionaryChange<TKey, TOuter>.Change(key, oldValue, value));
                break;
            }

            case ObservableDictionaryChangeType.ItemRemoved:
            {
                var key = change.Key!;

                if (!_items.TryGetValue(key, out var oldValue))
                {
                    // Same disagreement as the change above: rebuild and say the change was not
                    // described. A removal never maps anything - the record names the key and this view
                    // is keyed by the key, so what came out is the value the cache already held.
                    Resync();
                    break;
                }

                _items.Remove(key);
                _batch.Record(ObservableDictionaryChange<TKey, TOuter>.Remove(key, oldValue));
                break;
            }

            case ObservableDictionaryChangeType.DictionaryCleared:
                _items.Clear();
                _batch.Record(ObservableDictionaryChange<TKey, TOuter>.Clear());
                break;

            default:
                // UntrackedChanges, and any kind a later version adds: the change was not described, so the
                // only safe answer is to rebuild from the source and say so - the same conservative answer
                // ObservableDictionaryDependencies.Mark gives for an unknown record.
                Resync();
                break;
        }
    }

    /// <summary>Rebuilds the mapped contents from the source, and records that the change was not
    /// tracked.</summary>
    private void Resync()
    {
        _items.Clear();

        foreach (var item in _inner)
        {
            _items[item.Key] = _selector(item.Value);
        }

        _batch.Record(ObservableDictionaryChange<TKey, TOuter>.Untracked());
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_batch.IsDisposed, this);
}

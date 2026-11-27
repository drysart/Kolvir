namespace Kolvir.Feature.Observables;

/// <summary>
/// The one implementation of an observable dictionary: a <see cref="Dictionary{TKey, TValue}"/> and the
/// batch that carries its records to the handlers.
/// </summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TValue">The type of the values.</typeparam>
/// <remarks>
/// <para>
/// Internal, and handed out as <see cref="IObservableDictionary{TKey, TValue}"/> and
/// <see cref="IReadOnlyObservableDictionary{TKey, TValue}"/>, so the class itself is not part of the
/// contract.
/// </para>
/// <para>
/// The storage is a <see cref="Dictionary{TKey, TValue}"/>, so enumeration is version-checked and a view
/// obtained from <see cref="Keys"/> or <see cref="Values"/> is live, as it is anywhere else in the BCL.
/// Everything about how a change reaches a handler is
/// <see cref="ObservableChangeBatch{TChange, TArgs}"/>; what is left here is the record each call raises
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 7.4).
/// </para>
/// <para>
/// <b>The set view is a second stream over the same changes.</b> This class is also an
/// <see cref="IReadOnlyObservableSet{T}"/> of its pairs, so it carries a second
/// <see cref="ObservableChangeBatch{TChange, TArgs}"/> for the set handlers and an internal handler on the
/// dictionary batch: every delivered dictionary event is translated into the set records an observer hears,
/// through <see cref="ObservableDictionaryPairSet{TKey, TValue}"/>, which is also where the pair comparer
/// and the set algebra live. The set event therefore cannot describe a change the dictionary event did not,
/// and an untracked dictionary event becomes an untracked set event rather than being described anyway
/// (section 7.7).
/// </para>
/// </remarks>
internal sealed class ObservableDictionary<TKey, TValue>
    : IObservableDictionary<TKey, TValue>,
      IObservableChangeTarget<ObservableDictionaryChange<TKey, TValue>, ObservableDictionaryChangedEventArgs<TKey, TValue>>,
      IObservableChangeTarget<ObservableSetChange<KeyValuePair<TKey, TValue>>, ObservableSetChangedEventArgs<KeyValuePair<TKey, TValue>>>
    where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _items;

    /// <summary>What decides that two entries are the same element of the set view.</summary>
    private readonly IEqualityComparer<KeyValuePair<TKey, TValue>> _pairComparer;

    private readonly ObservableDictionaryDependencies<TKey, TValue> _dependencies;
    private readonly ObservableChangeBatch<ObservableDictionaryChange<TKey, TValue>, ObservableDictionaryChangedEventArgs<TKey, TValue>> _batch;
    private readonly ObservableChangeBatch<ObservableSetChange<KeyValuePair<TKey, TValue>>, ObservableSetChangedEventArgs<KeyValuePair<TKey, TValue>>> _setBatch;

    /// <summary>Creates an empty dictionary.</summary>
    /// <param name="comparer">What decides that two keys are the same key, or null for the type's
    /// own.</param>
    internal ObservableDictionary(IEqualityComparer<TKey>? comparer)
        : this([], comparer)
    {
    }

    /// <summary>Creates a dictionary with contents.</summary>
    /// <param name="items">What it holds.</param>
    /// <param name="comparer">What decides that two keys are the same key, or null for the type's
    /// own.</param>
    /// <exception cref="ArgumentException">Two items have keys the comparer calls equal, which is
    /// <see cref="Dictionary{TKey, TValue}"/>'s own rule.</exception>
    /// <remarks>Creating a dictionary with contents raises nothing: there is nobody registered yet, and
    /// the items are the starting state rather than a change to it.</remarks>
    internal ObservableDictionary(IEnumerable<KeyValuePair<TKey, TValue>> items, IEqualityComparer<TKey>? comparer)
    {
        _items = new Dictionary<TKey, TValue>(comparer);

        foreach (var item in items)
        {
            _items.Add(item.Key, item.Value);
        }

        // The dictionary's own comparer, which is the one the storage settled on - a null argument means
        // the type's default, and the dependencies have to key by the same rule.
        _dependencies = new ObservableDictionaryDependencies<TKey, TValue>(_items.Comparer);

        // The set view's identity: the key under that same comparer, the value under equality (7.7).
        _pairComparer = ObservableDictionaryPairSet<TKey, TValue>.CreateComparer(_items.Comparer);

        _batch = new(this);
        _setBatch = new(this);

        // Registered before any external handler, so the set stream is recorded from the same record the
        // dictionary handlers are about to hear - and so a dictionary handler that throws cannot leave it
        // undescribed. The batch owns the registration and drops it on disposal; there is nothing to
        // unregister separately.
        _batch.AddHandler(OnDictionaryChanged);
    }

    /// <summary>What decides that two keys are the same key.</summary>
    public IEqualityComparer<TKey> Comparer => _items.Comparer;

    /// <summary>What decides that two pairs are the same element of the set view.</summary>
    /// <remarks>
    /// Explicit because this one is not the member a dictionary caller sees: the public
    /// <see cref="Comparer"/> above is the key comparer, and this is the set base's, hidden by it. The two
    /// answer different questions - which keys are one key, and which pairs are one pair - and neither is a
    /// translation of the other.
    /// </remarks>
    IEqualityComparer<KeyValuePair<TKey, TValue>> IReadOnlyObservableSet<KeyValuePair<TKey, TValue>>.Comparer =>
        _pairComparer;

    /// <inheritdoc/>
    public bool IsDisposed => _batch.IsDisposed;

    /// <inheritdoc/>
    /// <remarks>
    /// A constant of the dictionary and not a read of anything, so it publishes no dependency and stays
    /// readable once it is dead.
    /// </remarks>
    public bool IsReadOnly => false;

    /// <inheritdoc/>
    public int ChangeBatchDepth => _batch.ChangeBatchDepth;

    /// <inheritdoc/>
    /// <remarks>The bound applies to both streams: a set event is an event from this dictionary too, so a
    /// caller that tuned the cap gets the same bound from each.</remarks>
    public int MaxCollectionBatchSize
    {
        get => _batch.MaxCollectionBatchSize;
        set
        {
            _batch.MaxCollectionBatchSize = value;
            _setBatch.MaxCollectionBatchSize = value;
        }
    }

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
    /// <remarks>Publishes the structure dependency, because a key's identity cannot change without it
    /// being a different key.</remarks>
    public ICollection<TKey> Keys
    {
        get
        {
            ThrowIfDisposed();

            _dependencies.PublishCount();
            return _items.Keys;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Publishes the contents dependency, because a value changes every time somebody edits
    /// one.</remarks>
    public ICollection<TValue> Values
    {
        get
        {
            ThrowIfDisposed();

            _dependencies.PublishContents();
            return _items.Values;
        }
    }

    /// <summary>The read-only spelling of <see cref="Keys"/>, which is a different member with a
    /// different type.</summary>
    /// <remarks>
    /// Explicit because an interface implementation has to match the member's type exactly, and
    /// <see cref="IReadOnlyDictionary{TKey, TValue}"/> spells these as <see cref="IEnumerable{T}"/> where
    /// <see cref="IDictionary{TKey, TValue}"/> spells them as collections. Going through the public
    /// property keeps one place that publishes.
    /// </remarks>
    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

    /// <summary>The read-only spelling of <see cref="Values"/>, for the reason
    /// <see cref="Keys"/> gives.</summary>
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

    /// <inheritdoc/>
    /// <remarks>
    /// Reading publishes the key's dependency, and only when the key is there: a key that is gone is the
    /// wrong shape of question to remember, and <see cref="TryGetValue"/> and
    /// <see cref="ContainsKey"/> are the shapes that survive it. Writing raises an
    /// <see cref="ObservableDictionaryChangeType.ItemAdded"/> for an absent key and an
    /// <see cref="ObservableDictionaryChangeType.ItemChanged"/> for a present one, unless the new value
    /// equals the old.
    /// </remarks>
    public TValue this[TKey key]
    {
        get
        {
            ThrowIfDisposed();

            if (!_items.TryGetValue(key, out var value))
            {
                throw new KeyNotFoundException(
                    $"The key {key} is not in the dictionary. A read of an absent key publishes nothing, "
                    + "because a key that is gone is not an address to depend on - ask with TryGetValue or "
                    + "ContainsKey to be woken by it appearing.");
            }

            _dependencies.PublishKey(key);
            return value;
        }

        set
        {
            ThrowIfDisposed();

            if (_items.TryGetValue(key, out var oldValue))
            {
                if (EqualityComparer<TValue>.Default.Equals(oldValue, value))
                {
                    return;
                }

                _items[key] = value;
                _batch.Record(ObservableDictionaryChange<TKey, TValue>.Change(key, oldValue, value));
                return;
            }

            _items.Add(key, value);
            _batch.Record(ObservableDictionaryChange<TKey, TValue>.Add(key, value));
        }
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">The comparer says the key is already there.</exception>
    public void Add(TKey key, TValue value)
    {
        ThrowIfDisposed();

        // The storage throws for a key that is already there, with the message its own callers expect.
        _items.Add(key, value);
        _batch.Record(ObservableDictionaryChange<TKey, TValue>.Add(key, value));
    }

    /// <inheritdoc/>
    public bool TryAdd(TKey key, TValue value)
    {
        ThrowIfDisposed();

        if (!_items.TryAdd(key, value))
        {
            return false;
        }

        _batch.Record(ObservableDictionaryChange<TKey, TValue>.Add(key, value));
        return true;
    }

    /// <summary>The <see cref="ICollection{T}"/> spelling of <see cref="Add(TKey, TValue)"/>.</summary>
    /// <param name="item">The key and value to add.</param>
    /// <exception cref="ArgumentException">The comparer says the key is already there.</exception>
    void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

    /// <inheritdoc/>
    /// <remarks>The record carries the value the key had, which is what an observer's copy held.</remarks>
    public bool Remove(TKey key)
    {
        ThrowIfDisposed();

        if (!_items.TryGetValue(key, out var value))
        {
            return false;
        }

        _items.Remove(key);
        _batch.Record(ObservableDictionaryChange<TKey, TValue>.Remove(key, value));
        return true;
    }

    /// <summary>Removes the pair, if the key is there with exactly that value.</summary>
    /// <param name="item">The key, and the value it must have.</param>
    /// <returns>Whether it was removed.</returns>
    /// <remarks>
    /// Conditional, as <see cref="ICollection{T}.Remove"/> says: a key whose value differs from the
    /// pair's is left alone and nothing is raised, because nothing changed (section 7.2.3).
    /// </remarks>
    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        ThrowIfDisposed();

        if (!_items.TryGetValue(item.Key, out var value)
            || !EqualityComparer<TValue>.Default.Equals(value, item.Value))
        {
            return false;
        }

        _items.Remove(item.Key);
        _batch.Record(ObservableDictionaryChange<TKey, TValue>.Remove(item.Key, value));
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>One record however many keys there were, and nothing at all for a dictionary already
    /// empty.</remarks>
    public void Clear()
    {
        ThrowIfDisposed();

        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        _batch.Record(ObservableDictionaryChange<TKey, TValue>.Clear());
    }

    /// <inheritdoc/>
    /// <remarks>Publishes the key's dependency before answering, so a reader that asked about an absent
    /// key is woken by the key appearing - and a value change wakes this too, deliberately
    /// (section 7.2.4).</remarks>
    public bool ContainsKey(TKey key)
    {
        ThrowIfDisposed();

        _dependencies.PublishKey(key);
        return _items.ContainsKey(key);
    }

    /// <inheritdoc/>
    /// <remarks>Publishes the key's dependency before answering, whether or not the key is there.</remarks>
    public bool TryGetValue(TKey key, out TValue value)
    {
        ThrowIfDisposed();

        _dependencies.PublishKey(key);
        return _items.TryGetValue(key, out value!);
    }

    /// <inheritdoc/>
    /// <remarks>Publishes the contents dependency, because it compares the values as well as the
    /// keys, and answers with the set view's rule: the key must be present under the dictionary's own
    /// comparer and its value must equal the pair's. The BCL's
    /// <see cref="ICollection{T}.Contains"/> would answer the key half with
    /// <see cref="EqualityComparer{TKey}.Default"/>, which disagrees with the dictionary for any other
    /// comparer (section 7.7).</remarks>
    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return ObservableDictionaryPairSet<TKey, TValue>.Contains(_items, item);
    }

    /// <inheritdoc/>
    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        ((ICollection<KeyValuePair<TKey, TValue>>)_items).CopyTo(array, arrayIndex);
    }

    /// <inheritdoc/>
    /// <remarks>Publishes the contents dependency, because an enumeration answers from every value. The
    /// enumerator is the storage's own, so mutating during the enumeration throws
    /// <see cref="InvalidOperationException"/>.</remarks>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return _items.GetEnumerator();
    }

    /// <inheritdoc/>
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    /// <remarks>Publishes the contents dependency, because it answers from every pair on at least one
    /// side. The pair comparer is the set view's, so the same keys with a different value on either side
    /// make the answer false (section 7.7).</remarks>
    public bool SetEquals(IEnumerable<KeyValuePair<TKey, TValue>> other)
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return ObservableDictionaryPairSet<TKey, TValue>.SetEquals(_items, _pairComparer, other);
    }

    /// <inheritdoc/>
    /// <remarks>Publishes the contents dependency, for the reason <see cref="SetEquals"/> gives.</remarks>
    public bool IsSubsetOf(IEnumerable<KeyValuePair<TKey, TValue>> other)
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return ObservableDictionaryPairSet<TKey, TValue>.IsSubsetOf(_items, _pairComparer, other);
    }

    /// <inheritdoc/>
    /// <remarks>Publishes the contents dependency, for the reason <see cref="SetEquals"/> gives.</remarks>
    public bool IsSupersetOf(IEnumerable<KeyValuePair<TKey, TValue>> other)
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return ObservableDictionaryPairSet<TKey, TValue>.IsSupersetOf(_items, other);
    }

    /// <inheritdoc/>
    /// <remarks>Publishes the contents dependency, for the reason <see cref="SetEquals"/> gives.</remarks>
    public bool IsProperSubsetOf(IEnumerable<KeyValuePair<TKey, TValue>> other)
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return ObservableDictionaryPairSet<TKey, TValue>.IsProperSubsetOf(_items, _pairComparer, other);
    }

    /// <inheritdoc/>
    /// <remarks>Publishes the contents dependency, for the reason <see cref="SetEquals"/> gives.</remarks>
    public bool IsProperSupersetOf(IEnumerable<KeyValuePair<TKey, TValue>> other)
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return ObservableDictionaryPairSet<TKey, TValue>.IsProperSupersetOf(_items, _pairComparer, other);
    }

    /// <inheritdoc/>
    /// <remarks>Publishes the contents dependency, for the reason <see cref="SetEquals"/> gives.</remarks>
    public bool Overlaps(IEnumerable<KeyValuePair<TKey, TValue>> other)
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return ObservableDictionaryPairSet<TKey, TValue>.Overlaps(_items, other);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Both batches are opened, so a caller that opened one sees one event from each stream rather than
    /// one from the dictionary and one per mutation from the set. They are opened together so neither can
    /// be left behind by a caller that only knows about one of them.
    /// </remarks>
    public void BeginChangeBatch()
    {
        ThrowIfDisposed();

        _batch.BeginChangeBatch();
        _setBatch.BeginChangeBatch();
    }

    /// <inheritdoc/>
    /// <remarks>The two are closed together, so neither can be left open by a caller that only knows
    /// about one of them.</remarks>
    public void EndChangeBatch()
    {
        ThrowIfDisposed();

        try
        {
            _batch.EndChangeBatch();
        }
        finally
        {
            _setBatch.EndChangeBatch();
        }
    }

    /// <inheritdoc/>
    public IDisposable AddObservableDictionaryChangedHandler(
        Action<ObservableDictionaryChangedEventArgs<TKey, TValue>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return _batch.AddHandler(handler);
    }

    /// <inheritdoc/>
    public void RemoveObservableDictionaryChangedHandler(
        Action<ObservableDictionaryChangedEventArgs<TKey, TValue>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _batch.RemoveHandler(handler);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The set view's handlers are registered on a batch of their own, exactly as the dictionary's are, so
    /// the token, the tolerance of a repeated removal and the refusal once the dictionary is disposed are
    /// the same and come from the same place.
    /// </remarks>
    public IDisposable AddObservableSetChangedHandler(
        Action<ObservableSetChangedEventArgs<KeyValuePair<TKey, TValue>>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return _setBatch.AddHandler(handler);
    }

    /// <inheritdoc/>
    public void RemoveObservableSetChangedHandler(
        Action<ObservableSetChangedEventArgs<KeyValuePair<TKey, TValue>>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _setBatch.RemoveHandler(handler);
    }

    /// <summary>
    /// Ends the dictionary's life: pending records are dropped, one last empty event says so, and every
    /// subscription is let go.
    /// </summary>
    /// <remarks>
    /// Both streams end, in that order: the dictionary's handlers hear their final event, during which the
    /// set batch records nothing, and then the set handlers hear theirs. A set handler is therefore not
    /// called after disposal, in the same sense a dictionary handler is not - the batch is marked disposed
    /// before its final event, refuses a later registration, and stops delivering.
    /// </remarks>
    public void Dispose()
    {
        try
        {
            _batch.Dispose();
        }
        finally
        {
            _setBatch.Dispose();
        }
    }

    /// <summary>What this dictionary is, for a diagnostic.</summary>
    public override string ToString() => $"an observable dictionary of {typeof(TKey).Name} to {typeof(TValue).Name}";

    /// <inheritdoc/>
    int IObservableChangeTarget<ObservableDictionaryChange<TKey, TValue>, ObservableDictionaryChangedEventArgs<TKey, TValue>>.Size =>
        _items.Count;

    /// <inheritdoc/>
    ObservableDictionaryChange<TKey, TValue>
        IObservableChangeTarget<ObservableDictionaryChange<TKey, TValue>, ObservableDictionaryChangedEventArgs<TKey, TValue>>.Untracked =>
        ObservableDictionaryChange<TKey, TValue>.Untracked();

    /// <inheritdoc/>
    bool IObservableChangeTarget<ObservableDictionaryChange<TKey, TValue>, ObservableDictionaryChangedEventArgs<TKey, TValue>>.IsUntracked(
        in ObservableDictionaryChange<TKey, TValue> change) =>
        change.ChangeType == ObservableDictionaryChangeType.UntrackedChanges;

    /// <inheritdoc/>
    ObservableDictionaryChangedEventArgs<TKey, TValue>
        IObservableChangeTarget<ObservableDictionaryChange<TKey, TValue>, ObservableDictionaryChangedEventArgs<TKey, TValue>>.CreateArgs(
            IReadOnlyList<ObservableDictionaryChange<TKey, TValue>> changes) =>
        new(this, changes);

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableDictionaryChange<TKey, TValue>, ObservableDictionaryChangedEventArgs<TKey, TValue>>.Mark(
        IReadOnlyList<ObservableDictionaryChange<TKey, TValue>> changes,
        int size) =>
        _dependencies.Mark(changes);

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableDictionaryChange<TKey, TValue>, ObservableDictionaryChangedEventArgs<TKey, TValue>>.RaiseDependencies() =>
        _dependencies.Raise();

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableDictionaryChange<TKey, TValue>, ObservableDictionaryChangedEventArgs<TKey, TValue>>.ReleaseDependencies() =>
        _dependencies.Release();

    /// <inheritdoc/>
    int IObservableChangeTarget<ObservableSetChange<KeyValuePair<TKey, TValue>>, ObservableSetChangedEventArgs<KeyValuePair<TKey, TValue>>>.Size =>
        _items.Count;

    /// <inheritdoc/>
    ObservableSetChange<KeyValuePair<TKey, TValue>>
        IObservableChangeTarget<ObservableSetChange<KeyValuePair<TKey, TValue>>, ObservableSetChangedEventArgs<KeyValuePair<TKey, TValue>>>.Untracked =>
        ObservableSetChange<KeyValuePair<TKey, TValue>>.Untracked();

    /// <inheritdoc/>
    bool IObservableChangeTarget<ObservableSetChange<KeyValuePair<TKey, TValue>>, ObservableSetChangedEventArgs<KeyValuePair<TKey, TValue>>>.IsUntracked(
        in ObservableSetChange<KeyValuePair<TKey, TValue>> change) =>
        change.ChangeType == ObservableSetChangeType.UntrackedChanges;

    /// <summary>The argument for one set delivery, naming this dictionary as the set that changed.</summary>
    /// <remarks>
    /// <see cref="ObservableSetChangedEventArgs{T}.Set"/> is therefore the dictionary itself, which is the
    /// same object the caller was handed and not a view standing in for it - a handler that reads it reads
    /// the dictionary, with the dependencies a dictionary read publishes.
    /// </remarks>
    ObservableSetChangedEventArgs<KeyValuePair<TKey, TValue>>
        IObservableChangeTarget<ObservableSetChange<KeyValuePair<TKey, TValue>>, ObservableSetChangedEventArgs<KeyValuePair<TKey, TValue>>>.CreateArgs(
            IReadOnlyList<ObservableSetChange<KeyValuePair<TKey, TValue>>> changes) =>
        new(this, changes);

    /// <summary>Marks nothing, because the set view publishes the dictionary's dependencies.</summary>
    /// <remarks>
    /// There is no second dependency set here on purpose. Every set question is answered from the
    /// dictionary - <c>Contains(pair)</c> and the six algebra questions publish the contents dependency the
    /// dictionary already has - and every set record exists because a dictionary record did, so the
    /// dictionary's own delivery marks exactly what a set reader is watching.
    /// </remarks>
    void IObservableChangeTarget<ObservableSetChange<KeyValuePair<TKey, TValue>>, ObservableSetChangedEventArgs<KeyValuePair<TKey, TValue>>>.Mark(
        IReadOnlyList<ObservableSetChange<KeyValuePair<TKey, TValue>>> changes,
        int size)
    {
    }

    /// <inheritdoc/>
    /// <remarks>Nothing to raise, for the reason <c>Mark</c> gives.</remarks>
    void IObservableChangeTarget<ObservableSetChange<KeyValuePair<TKey, TValue>>, ObservableSetChangedEventArgs<KeyValuePair<TKey, TValue>>>.RaiseDependencies()
    {
    }

    /// <inheritdoc/>
    /// <remarks>Nothing to release, for the reason <c>Mark</c> gives: the dependencies belong to the
    /// dictionary, and its own batch releases them.</remarks>
    void IObservableChangeTarget<ObservableSetChange<KeyValuePair<TKey, TValue>>, ObservableSetChangedEventArgs<KeyValuePair<TKey, TValue>>>.ReleaseDependencies()
    {
    }

    /// <summary>Records the set records one delivered dictionary event describes.</summary>
    /// <param name="args">The event the dictionary handlers are being called with.</param>
    /// <remarks>
    /// The one empty event - the one a disposal delivers - carries nothing and is ignored here; the set
    /// batch delivers its own final event when the dictionary's disposal reaches it.
    /// </remarks>
    private void OnDictionaryChanged(ObservableDictionaryChangedEventArgs<TKey, TValue> args) =>
        ObservableDictionaryPairSet<TKey, TValue>.RecordChanges(_setBatch, args.Changes);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_batch.IsDisposed, this);
}

namespace Kolvir.Feature.Observables;

/// <summary>
/// A dictionary that can be changed, which only the code that owns the state is handed.
/// </summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TValue">The type of the values.</typeparam>
/// <remarks>
/// <para>
/// What <see cref="Observable.CreateDictionary{TKey, TValue}()"/> returns. The owner of a model holds one
/// in a private field and exposes it as <see cref="IReadOnlyObservableDictionary{TKey, TValue}"/>, so a
/// consumer can read it and watch it but cannot change it.
/// </para>
/// <para>
/// <b>Every change is reported at the granularity it happened.</b> A key arriving is one
/// <see cref="ObservableDictionaryChangeType.ItemAdded"/> carrying the key and its value; a key leaving is
/// one <see cref="ObservableDictionaryChangeType.ItemRemoved"/> carrying the key and the value it had; a
/// value written beside a key that stayed is one
/// <see cref="ObservableDictionaryChangeType.ItemChanged"/> with both values, raised only when the new
/// value differs; and a <c>Clear</c> is one
/// <see cref="ObservableDictionaryChangeType.DictionaryCleared"/> however many keys there were. A call
/// that changes nothing raises nothing.
/// </para>
/// <para>
/// <b>Delivery is ordered, never nested.</b> A change made from inside a handler is queued and delivered
/// after the event that handler is hearing, so two handlers never see records out of order. The delivery
/// *call* is counted against <see cref="Observable.MaxEventStackSize"/>, because a handler can re-enter
/// one - by disposing a dictionary, or by changing a different collection, whose own drain then runs
/// inside that handler's frame (section 3.7).
/// </para>
/// <para>
/// <b>Batches suppress notifications, not changes.</b> Between <see cref="BeginChangeBatch"/> and
/// <see cref="EndChangeBatch"/> the dictionary changes as usual and collects every record; the outermost
/// end delivers them all in one event, in order. A batch is capped at
/// <see cref="MaxCollectionBatchSize"/> records: past that the collected records are dropped and the
/// event carries one <see cref="ObservableDictionaryChangeType.UntrackedChanges"/> record instead, and
/// nothing later in that batch is queued.
/// </para>
/// </remarks>
public interface IObservableDictionary<TKey, TValue>
    : IReadOnlyObservableDictionary<TKey, TValue>, IDictionary<TKey, TValue>
{
    /// <summary>How many keys the dictionary holds.</summary>
    /// <exception cref="ObjectDisposedException">The dictionary is disposed.</exception>
    /// <remarks>
    /// <para>
    /// Redeclared rather than merely inherited, and that is a language requirement rather than a style
    /// choice: <see cref="IDictionary{TKey, TValue}"/> and
    /// <see cref="IReadOnlyObservableDictionary{TKey, TValue}"/> both bring a <c>Count</c>, so a caller of
    /// <c>dictionary.Count</c> through this interface would be told the call is ambiguous. Six members get
    /// this treatment - <c>Count</c>, the indexer, <c>Keys</c>, <c>Values</c>, <c>ContainsKey</c> and
    /// <c>TryGetValue</c> - because a dictionary is the collection whose two shapes overlap the most.
    /// </para>
    /// <para>
    /// Reading publishes the structure dependency, so an expression over the size is re-run when a key is
    /// added, removed or cleared, and never by a value changing beside a key that stayed.
    /// </para>
    /// </remarks>
    new int Count { get; }

    /// <summary>The value beside <paramref name="key"/>.</summary>
    /// <param name="key">The key.</param>
    /// <value>The value there. Writing raises one
    /// <see cref="ObservableDictionaryChangeType.ItemAdded"/> when the key is absent and one
    /// <see cref="ObservableDictionaryChangeType.ItemChanged"/> when it is present and the new value
    /// differs.</value>
    /// <exception cref="KeyNotFoundException">The key is not in the dictionary, on a read.</exception>
    /// <exception cref="ObjectDisposedException">The dictionary is disposed.</exception>
    /// <remarks>
    /// <para>
    /// Redeclared because <see cref="IReadOnlyDictionary{TKey, TValue}"/> brings a get-only indexer and
    /// <see cref="IDictionary{TKey, TValue}"/> a settable one, which are two different members with the
    /// same name.
    /// </para>
    /// <para>
    /// Reading publishes the key's dependency, and only when the key is there: an indexer read of an
    /// absent key throws, and a key that is gone is the wrong shape of question to remember. A reader that
    /// wants to be woken by a key appearing asks with <see cref="TryGetValue"/> or
    /// <see cref="ContainsKey"/>, which publish before they answer (section 7.4).
    /// </para>
    /// </remarks>
    new TValue this[TKey key] { get; set; }

    /// <summary>The keys, as a live view.</summary>
    /// <exception cref="ObjectDisposedException">The dictionary is disposed.</exception>
    /// <remarks>
    /// Redeclared for the reason <see cref="Count"/> gives: the two inherited <c>Keys</c> have different
    /// types. Reading publishes the structure dependency - a key's identity cannot change without it being
    /// a different key - and enumerating what the view returned afterwards is the reader's own read, with
    /// <see cref="Dictionary{TKey, TValue}"/>'s semantics (section 7.2.4).
    /// </remarks>
    new ICollection<TKey> Keys { get; }

    /// <summary>The values, as a live view.</summary>
    /// <exception cref="ObjectDisposedException">The dictionary is disposed.</exception>
    /// <remarks>
    /// Redeclared for the reason <see cref="Count"/> gives. Reading publishes the contents dependency,
    /// because a value changes every time somebody edits one, and enumerating what the view returned
    /// afterwards is the reader's own read.</remarks>
    new ICollection<TValue> Values { get; }

    /// <summary>Whether <paramref name="key"/> is in the dictionary.</summary>
    /// <param name="key">The key to look for.</param>
    /// <exception cref="ObjectDisposedException">The dictionary is disposed.</exception>
    /// <remarks>
    /// Redeclared for the reason <see cref="Count"/> gives. Reading publishes the key's dependency before
    /// answering, so a reader that asked about an absent key is woken by the key appearing - and it is the
    /// same entry a <c>TryGetValue</c> for that key holds, deliberately: presence and value are one thing
    /// to everyone who holds it, so a value change wakes this too (section 7.2.4).
    /// </remarks>
    new bool ContainsKey(TKey key);

    /// <summary>Reads the value beside <paramref name="key"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value there, or <c>default</c> when the key is absent.</param>
    /// <returns>Whether the key was there.</returns>
    /// <exception cref="ObjectDisposedException">The dictionary is disposed.</exception>
    /// <remarks>Redeclared for the reason <see cref="Count"/> gives. Publishes the key's dependency before
    /// answering, whether or not the key is there.</remarks>
    new bool TryGetValue(TKey key, out TValue value);

    /// <summary>How many batches are open. Zero outside any batch.</summary>
    /// <remarks>
    /// On the interface rather than hidden, because a library that changes a dictionary on someone else's
    /// behalf wants to know whether it is already inside a batch before opening one that would not add
    /// anything.
    /// </remarks>
    int ChangeBatchDepth { get; }

    /// <summary>How many records one event from this dictionary may carry before it stops describing
    /// them.</summary>
    /// <value>
    /// <see cref="Observable.Default.MaxCollectionBatchSize"/> for a dictionary that has not been
    /// changed, and whatever was last assigned for one that has.
    /// </value>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than one, which would cap every
    /// event at nothing and make every batch a rescan.</exception>
    /// <remarks>
    /// The dictionary owns this, and it is copied from the default when it is created: setting it here
    /// tunes this dictionary's observers, and changing
    /// <see cref="Observable.Default.MaxCollectionBatchSize"/> tunes the ones dictionaries created
    /// afterwards will have.
    /// </remarks>
    int MaxCollectionBatchSize { get; set; }

    /// <summary>Starts a batch, which stops the dictionary delivering until it ends.</summary>
    /// <exception cref="ObjectDisposedException">The dictionary is disposed.</exception>
    /// <remarks>
    /// Nesting is allowed and counted. Open one per caller that needs it and close it in the same order;
    /// only the outermost <see cref="EndChangeBatch"/> delivers, so an outer batch that several inner ones
    /// sit inside still produces one event.
    /// </remarks>
    void BeginChangeBatch();

    /// <summary>Ends a batch, delivering what it collected when it was the outermost one.</summary>
    /// <exception cref="ObjectDisposedException">The dictionary is disposed.</exception>
    /// <exception cref="InvalidOperationException">No batch is open.</exception>
    /// <remarks>
    /// A batch that collected nothing delivers nothing. A dictionary disposed with a batch still open
    /// drops what it had not delivered, and the batch is gone with it.
    /// </remarks>
    void EndChangeBatch();

    /// <summary>Adds <paramref name="key"/> with <paramref name="value"/> if nothing is there.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value to put beside it.</param>
    /// <returns>Whether it was added; <c>false</c> when the comparer says the key is already there, and
    /// then nothing changed and nothing was raised.</returns>
    /// <exception cref="ObjectDisposedException">The dictionary is disposed.</exception>
    /// <remarks>
    /// On this interface because its absence is the one most felt: <see cref="IDictionary{TKey, TValue}"/>
    /// offers <c>Add</c>, which throws on a present key, and the indexer, which replaces. Add-if-absent
    /// would otherwise be a lookup, a decision and an exception to catch
    /// (dev-docs/OBSERVABLE-COLLECTIONS.md section 7.1).
    /// </remarks>
    bool TryAdd(TKey key, TValue value);
}

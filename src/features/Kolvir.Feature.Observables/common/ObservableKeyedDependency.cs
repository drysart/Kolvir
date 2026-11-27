namespace Kolvir.Feature.Observables;

/// <summary>
/// One watched address: the key it was read by, kept instead of the name that key would have had in a
/// diagnostic.
/// </summary>
/// <typeparam name="TKey">What addresses the read: a list's index, a set's item, a dictionary's key.</typeparam>
/// <remarks>
/// <para>
/// A run holds an entry for every address it read, so what an entry carries is paid for once per address
/// on screen. The name is only ever needed to describe a refusal, which happens when a chain has already
/// gone wrong, so the key is kept and the text is built in <see cref="ToString"/> from it - one string
/// per entry that is never made, rather than one that is kept for the entry's whole life.
/// </para>
/// <para>
/// The key is stored inline rather than boxed: four bytes for a list's index, eight for a reference
/// address, plus one for the kind. An index therefore takes one eight-byte slot and a reference address
/// two, which is what an entry costs on top of a plain <see cref="ObservableDependency"/>.
/// </para>
/// <para>
/// The text is the collection's, not the key's: <see cref="ObservableKeyedDependencyKind"/> is which
/// collection, and the wording lives here because there is one place to keep it in step with the
/// diagnostic it appears in.
/// </para>
/// </remarks>
internal sealed class ObservableKeyedDependency<TKey> : ObservableDependency
    where TKey : notnull
{
    private readonly TKey _key;
    private readonly ObservableKeyedDependencyKind _kind;

    /// <summary>Creates an entry for one address of one collection.</summary>
    /// <param name="key">The address that was read.</param>
    /// <param name="kind">Which collection it was read from, for the name.</param>
    /// <param name="onWatched">What to call when a handler is added to an entry that had none.</param>
    /// <param name="onReleased">What to call when the last handler is removed.</param>
    internal ObservableKeyedDependency(
        TKey key,
        ObservableKeyedDependencyKind kind,
        Action? onWatched = null,
        Action? onReleased = null)
        : base(null, onWatched, onReleased)
    {
        _key = key;
        _kind = kind;
    }

    /// <summary>What was read, built now rather than kept.</summary>
    public override string ToString() => _kind switch
    {
        ObservableKeyedDependencyKind.Index => $"index {_key} of an observable list",
        ObservableKeyedDependencyKind.Item => $"item {_key} of an observable set",
        _ => $"key {_key} of an observable dictionary",
    };
}

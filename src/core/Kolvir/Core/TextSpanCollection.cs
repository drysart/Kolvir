using System.Collections;

namespace Kolvir;

/// <summary>
/// Ordered span collection of an <see cref="RichLabel"/>. Adding or removing a span
/// links it to the owning label, syncs the inline controls it contributes and re-runs layout.
/// </summary>
public sealed class TextSpanCollection : IList<TextSpan>
{
    private readonly RichLabel _owner;
    private readonly List<TextSpan> _items = new();

    internal TextSpanCollection(RichLabel owner) => _owner = owner;

    /// <summary>
    /// The span at <paramref name="index"/>. Assigning unlinks the span it replaces and links the
    /// new one, so the inline controls and the layout follow in both directions.
    /// </summary>
    /// <exception cref="ArgumentNullException">The assigned span is null.</exception>
    public TextSpan this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var existing = _items[index];
            if (ReferenceEquals(existing, value))
            {
                return;
            }

            existing.Owner = null;
            _owner.OnSpanRemoved(existing);
            _items[index] = value;
            value.Owner = _owner;
            _owner.OnSpanAdded(value);
        }
    }

    /// <summary>The number of spans in the label.</summary>
    public int Count => _items.Count;

    /// <summary>
    /// Always false: the collection is meant to be edited while the label is on screen.
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Appends <paramref name="item"/> and links it to the label. Adding a span the label already
    /// owns is a no-op, so a handler that re-adds its own span does not duplicate it.
    /// </summary>
    /// <param name="item">The span to append.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public void Add(TextSpan item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (ReferenceEquals(item.Owner, _owner) && _items.Contains(item))
        {
            return;
        }

        _items.Add(item);
        item.Owner = _owner;
        _owner.OnSpanAdded(item);
    }

    /// <summary>Appends each span in <paramref name="items"/> in turn.</summary>
    /// <param name="items">The spans to append, in flow order.</param>
    /// <exception cref="ArgumentNullException">One of the spans is null.</exception>
    public void AddRange(params TextSpan[] items)
    {
        foreach (var item in items)
        {
            Add(item);
        }
    }

    /// <summary>
    /// Inserts <paramref name="item"/> at <paramref name="index"/> and links it to the label.
    /// </summary>
    /// <param name="index">The position to insert at, in flow order.</param>
    /// <param name="item">The span to insert.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public void Insert(int index, TextSpan item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Insert(index, item);
        item.Owner = _owner;
        _owner.OnSpanAdded(item);
    }

    /// <summary>
    /// Removes <paramref name="item"/> and unlinks it from the label, dropping the inline control it
    /// contributed, if any.
    /// </summary>
    /// <param name="item">The span to remove.</param>
    /// <returns>True when the span was in the collection; false when it was not, or was null.</returns>
    public bool Remove(TextSpan item)
    {
        if (item is null || !_items.Remove(item))
        {
            return false;
        }

        item.Owner = null;
        _owner.OnSpanRemoved(item);
        return true;
    }

    /// <summary>Removes the span at <paramref name="index"/> and unlinks it from the label.</summary>
    /// <param name="index">The position of the span to remove.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is outside the collection.
    /// </exception>
    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        item.Owner = null;
        _owner.OnSpanRemoved(item);
    }

    /// <summary>
    /// Removes every span, unlinks each one and so removes the inline controls they contributed.
    /// Does nothing when the collection is already empty.
    /// </summary>
    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        var removed = _items.ToArray();
        _items.Clear();
        foreach (var item in removed)
        {
            item.Owner = null;
            _owner.OnSpanRemoved(item);
        }
    }

    /// <summary>
    /// Determines whether <paramref name="item"/> is in the collection, by reference.
    /// </summary>
    /// <param name="item">The span to look for.</param>
    /// <returns>True when the label holds the span.</returns>
    public bool Contains(TextSpan item) => _items.Contains(item);

    /// <summary>
    /// The position of <paramref name="item"/> in flow order, or -1 when it is not in the
    /// collection.
    /// </summary>
    /// <param name="item">The span to look for.</param>
    /// <returns>The zero-based index of the span, or -1.</returns>
    public int IndexOf(TextSpan item) => _items.IndexOf(item);

    /// <summary>
    /// Copies the spans into <paramref name="array"/>, starting at <paramref name="arrayIndex"/>.
    /// </summary>
    /// <param name="array">The destination array.</param>
    /// <param name="arrayIndex">The index in <paramref name="array"/> to start copying at.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="array"/> has no room for the spans from <paramref name="arrayIndex"/> on.
    /// </exception>
    public void CopyTo(TextSpan[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <summary>Enumerates the spans in flow order.</summary>
    /// <returns>An enumerator over the spans as they are at the time it is taken.</returns>
    public IEnumerator<TextSpan> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

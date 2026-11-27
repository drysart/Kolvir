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
    /// Moves the span at <paramref name="fromIndex"/> to <paramref name="toIndex"/> without
    /// unlinking it from the label or disturbing the inline control it contributed.
    /// </summary>
    /// <param name="fromIndex">The span's position now.</param>
    /// <param name="toIndex">The position it should occupy afterwards.</param>
    /// <remarks>
    /// <para>
    /// This is the span equivalent of <see cref="ControlCollection.Move"/>: the span keeps its
    /// <see cref="TextSpan.Owner"/>, its inline control stays parented to the label, and the control
    /// is moved inside <see cref="Control.Controls"/> so the two orders agree. A reorder is therefore
    /// a reorder and not a remove followed by an insert, which would unparent and re-parent every
    /// control in the moved region.
    /// </para>
    /// <para>
    /// Both indices address the collection as it is now, so moving the first of three spans to the
    /// end is <c>Move(0, 2)</c>. A move to the index the span already occupies does nothing at all.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Either index is outside the collection.</exception>
    public void Move(int fromIndex, int toIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fromIndex, _items.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(toIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(toIndex, _items.Count);

        if (fromIndex == toIndex)
        {
            return;
        }

        var item = _items[fromIndex];
        _items.RemoveAt(fromIndex);
        _items.Insert(toIndex, item);
        MoveControl(item, toIndex);
        _owner.OnSpanMoved();
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
    /// Moves the inline control of <paramref name="item"/> inside the label's child collection so it
    /// keeps the same order relative to the other span controls.
    /// </summary>
    /// <param name="item">The span that moved.</param>
    /// <param name="toIndex">Its new position among the spans.</param>
    /// <remarks>
    /// The label lays spans out from its own list, but <see cref="Control.Controls"/> also has an
    /// order that decides painting and hit testing. Moving the control - rather than removing and
    /// adding it - is what keeps its parent, its state and anything a container keeps per child. A
    /// label may hold controls that are not spans at all, so the control is placed relative to the
    /// nearest span control before or after it and everything else is left where it is.
    /// </remarks>
    private void MoveControl(TextSpan item, int toIndex)
    {
        if (item.Control is not { } control || !ReferenceEquals(control.Parent, _owner))
        {
            return;
        }

        var current = _owner.Controls.IndexOf(control);

        if (current < 0)
        {
            return;
        }

        int? target = null;

        for (int i = toIndex - 1; i >= 0; i--)
        {
            if (_items[i].Control is { } previous && ReferenceEquals(previous.Parent, _owner))
            {
                target = _owner.Controls.IndexOf(previous) + 1;
                break;
            }
        }

        if (target is null)
        {
            for (int i = toIndex + 1; i < _items.Count; i++)
            {
                if (_items[i].Control is { } next && ReferenceEquals(next.Parent, _owner))
                {
                    target = _owner.Controls.IndexOf(next);
                    break;
                }
            }
        }

        if (target is { } index)
        {
            // ControlCollection.Move addresses the collection as it stands after the move, so a
            // control moving forward passes one index fewer than "after the control before it".
            if (current < index)
            {
                index--;
            }

            index = Math.Clamp(index, 0, _owner.Controls.Count - 1);

            if (index != current)
            {
                _owner.Controls.Move(current, index);
            }
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

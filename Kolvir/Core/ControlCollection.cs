using System.Collections;

namespace Kolvir;

/// <summary>
/// Ordered child control collection. Adding or removing a control updates the parent
/// link and triggers layout on the owning control.
/// </summary>
public sealed class ControlCollection : IList<Control>
{
    private readonly Control _owner;
    private readonly List<Control> _items = new();

    internal ControlCollection(Control owner) => _owner = owner;

    /// <summary>
    /// Gets or replaces the child at <paramref name="index"/>. Replacing a child detaches the old
    /// one from this control and attaches the new one, running the same add and remove handling as
    /// <see cref="Remove"/> and <see cref="Add"/>, so layout and painting follow.
    /// </summary>
    /// <param name="index">Position in the collection; later positions paint on top.</param>
    /// <returns>The child at that position.</returns>
    /// <exception cref="ArgumentNullException">The assigned value is null.</exception>
    public Control this[int index]
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

            existing.Parent = null;
            _owner.OnControlRemoved(existing);
            _items[index] = value;
            value.Parent = _owner;
            _owner.OnControlAdded(value);
        }
    }

    /// <summary>Number of children in the collection.</summary>
    public int Count => _items.Count;

    /// <summary>Always false: the collection is read-write.</summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Adds <paramref name="item"/> at the end of the z-order, which puts it on top of its siblings,
    /// and makes this collection's control its parent.
    /// </summary>
    /// <param name="item">The control to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public void Add(Control item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        item.Parent = _owner;
        _owner.OnControlAdded(item);
    }

    /// <summary>
    /// Adds several controls in order, each as if <see cref="Add"/> had been called for it.
    /// </summary>
    /// <param name="items">The controls to add.</param>
    /// <remarks>
    /// Each control is added on its own, so a container that adds many at once should wrap the call
    /// in <see cref="Control.SuspendLayout"/> and <see cref="Control.ResumeLayout()"/> to pay for
    /// one layout pass rather than one per child.
    /// </remarks>
    public void AddRange(params Control[] items)
    {
        foreach (var item in items)
        {
            Add(item);
        }
    }

    /// <summary>
    /// Inserts <paramref name="item"/> at <paramref name="index"/>, which places it behind the
    /// children that follow it in the z-order, and makes this collection's control its parent.
    /// </summary>
    /// <param name="index">Position to insert at.</param>
    /// <param name="item">The control to insert.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public void Insert(int index, Control item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Insert(index, item);
        item.Parent = _owner;
        _owner.OnControlAdded(item);
    }

    /// <summary>
    /// Removes <paramref name="item"/> and detaches it from this collection's control.
    /// </summary>
    /// <param name="item">The control to remove.</param>
    /// <returns>
    /// True when the control was in the collection; false when it was not, or was null.
    /// </returns>
    public bool Remove(Control item)
    {
        if (item is null || !_items.Remove(item))
        {
            return false;
        }

        item.Parent = null;
        _owner.OnControlRemoved(item);
        return true;
    }

    /// <summary>
    /// Removes the child at <paramref name="index"/> and detaches it from this collection's
    /// control.
    /// </summary>
    /// <param name="index">Position of the child to remove.</param>
    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        item.Parent = null;
        _owner.OnControlRemoved(item);
    }

    /// <summary>
    /// Removes every child, detaching each one. The children themselves are not disposed, so they
    /// can be attached elsewhere.
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
            item.Parent = null;
            _owner.OnControlRemoved(item);
        }
    }

    /// <summary>Answers whether <paramref name="item"/> is one of the children.</summary>
    /// <param name="item">The control to look for.</param>
    /// <returns>True when it is in the collection.</returns>
    public bool Contains(Control item) => _items.Contains(item);

    /// <summary>
    /// Returns the position of <paramref name="item"/> in the z-order, or -1 when it is absent.
    /// </summary>
    /// <param name="item">The control to look for.</param>
    /// <returns>The index, or -1.</returns>
    public int IndexOf(Control item) => _items.IndexOf(item);

    /// <summary>
    /// Copies the children into <paramref name="array"/>, starting at
    /// <paramref name="arrayIndex"/>.
    /// </summary>
    /// <param name="array">The destination array.</param>
    /// <param name="arrayIndex">Index in the destination to start at.</param>
    public void CopyTo(Control[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <summary>Enumerates the children in z-order, back to front.</summary>
    /// <returns>An enumerator over the children.</returns>
    public IEnumerator<Control> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

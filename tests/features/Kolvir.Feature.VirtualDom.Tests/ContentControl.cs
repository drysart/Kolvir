using System.Collections;
using Kolvir;

namespace Kolvir.Feature.VirtualDom.Tests;

/// <summary>
/// A non-control element with a value, a handler pair and a control member, so the generated
/// description class and the collection reconciler can be watched against real state.
/// </summary>
[VirtualizableElement]
public sealed class TestElement
{
    private string _text = string.Empty;
    private Control? _control;
    private Action<object>? _click;

    /// <summary>The value the description assigns.</summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            TextSetCount++;
        }
    }

    /// <summary>How many times <see cref="Text"/> was assigned, which an unchanged render must not raise.</summary>
    public int TextSetCount { get; private set; }

    /// <summary>The control this element carries, if any.</summary>
    public Control? Control
    {
        get => _control;
        set
        {
            _control = value;
            ControlSetCount++;
        }
    }

    /// <summary>How many times <see cref="Control"/> was assigned.</summary>
    public int ControlSetCount { get; private set; }

    /// <summary>How many handlers the generated description has attached.</summary>
    public int AttachCount { get; private set; }

    /// <summary>How many handlers the generated description has detached.</summary>
    public int DetachCount { get; private set; }

    /// <summary>Attaches a click handler, the way a TextSpan does.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>A token that removes it.</returns>
    public IDisposable AddClickHandler(Action<object> handler)
    {
        _click += handler;
        AttachCount++;
        return new Subscription(this, handler);
    }

    /// <summary>Detaches a click handler.</summary>
    /// <param name="handler">The handler.</param>
    public void RemoveClickHandler(Action<object> handler)
    {
        _click -= handler;
        DetachCount++;
    }

    /// <summary>Raises the element's click event, so a test can tell whether a handler is attached.</summary>
    public void Raise() => _click?.Invoke(this);

    private sealed class Subscription(TestElement element, Action<object> handler) : IDisposable
    {
        public void Dispose() => element.RemoveClickHandler(handler);
    }
}

/// <summary>
/// A control with a get-only collection of <see cref="TestElement"/>, and a list that records every
/// structural change so a test can assert that an unchanged render did none.
/// </summary>
public sealed class ContentControl : Control
{
    /// <summary>The collection a node describes.</summary>
    public ContentList<TestElement> Items { get; } = new();
}

/// <summary>An <see cref="IList{T}"/> that counts the mutations the reconciler performs.</summary>
/// <typeparam name="T">The element type.</typeparam>
public sealed class ContentList<T> : IList<T>
    where T : class
{
    private readonly List<T> _items = [];

    /// <summary>How many elements have been appended.</summary>
    public int AddCount { get; private set; }

    /// <summary>How many elements have been inserted.</summary>
    public int InsertCount { get; private set; }

    /// <summary>How many elements have been removed.</summary>
    public int RemoveCount { get; private set; }

    /// <summary>How many elements have been moved.</summary>
    public int MoveCount { get; private set; }

    /// <summary>How many elements have been replaced through the indexer.</summary>
    public int ReplaceCount { get; private set; }

    /// <summary>How many times the collection has been cleared.</summary>
    public int ClearCount { get; private set; }

    /// <inheritdoc />
    public T this[int index]
    {
        get => _items[index];
        set
        {
            _items[index] = value;
            ReplaceCount++;
        }
    }

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public void Add(T item)
    {
        _items.Add(item);
        AddCount++;
    }

    /// <inheritdoc />
    public void Clear()
    {
        _items.Clear();
        ClearCount++;
    }

    /// <inheritdoc />
    public bool Contains(T item) => _items.Contains(item);

    /// <inheritdoc />
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    public int IndexOf(T item) => _items.IndexOf(item);

    /// <inheritdoc />
    public void Insert(int index, T item)
    {
        _items.Insert(index, item);
        InsertCount++;
    }

    /// <summary>Moves an element without removing and re-adding it.</summary>
    /// <param name="fromIndex">The element's position now.</param>
    /// <param name="toIndex">The position it should occupy afterwards.</param>
    public void Move(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex)
        {
            return;
        }

        var item = _items[fromIndex];
        _items.RemoveAt(fromIndex);
        _items.Insert(toIndex, item);
        MoveCount++;
    }

    /// <inheritdoc />
    public bool Remove(T item)
    {
        int index = _items.IndexOf(item);

        if (index < 0)
        {
            return false;
        }

        _items.RemoveAt(index);
        RemoveCount++;
        return true;
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        _items.RemoveAt(index);
        RemoveCount++;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

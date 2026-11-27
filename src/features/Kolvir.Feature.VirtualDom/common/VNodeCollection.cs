namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// A control's get-only collection of elements, together with the descriptions a render gave it.
/// </summary>
/// <remarks>
/// <para>
/// This is the descriptor a generated node returns from <see cref="VNode.GetCollections"/>: the live
/// collection (so the reconciler can call the control's own <c>Insert</c>/<c>RemoveAt</c>, which run
/// whatever notifications the control has) and the descriptions in flow order. The generic
/// <see cref="Create{TElement}"/> is what keeps the descriptor typed: the collection's element type
/// is known at the call site, and every mutation is a direct <see cref="IList{T}"/> call rather than
/// a reflection lookup.
/// </para>
/// <para>
/// <see cref="Nodes"/> is null when the render did not assign the node's collection property at all.
/// That is "this render says nothing about the collection", not "make it empty"; an element already
/// in the collection is therefore left alone.
/// </para>
/// </remarks>
public abstract class VNodeCollection
{
    /// <summary>What the description knows about the element type this collection holds.</summary>
    public abstract ElementType ElementType { get; }

    /// <summary>The descriptions this render supplied, or null when it supplied none.</summary>
    public abstract IReadOnlyList<VElementDescription>? Nodes { get; }

    /// <summary>The number of elements currently in the live collection.</summary>
    public abstract int Count { get; }

    /// <summary>The element at <paramref name="index"/> in the live collection.</summary>
    /// <param name="index">The element's position.</param>
    /// <returns>The element, boxed if it is a value type (it is a class by construction).</returns>
    public abstract object At(int index);

    /// <summary>Removes the element at <paramref name="index"/> through the live collection.</summary>
    /// <param name="index">The element's position.</param>
    public abstract void RemoveAt(int index);

    /// <summary>Inserts <paramref name="element"/> at <paramref name="index"/> through the live collection.</summary>
    /// <param name="index">The position to insert at.</param>
    /// <param name="element">The element to insert.</param>
    public abstract void Insert(int index, object element);

    /// <summary>
    /// Moves the element at <paramref name="fromIndex"/> to <paramref name="toIndex"/>.
    /// </summary>
    /// <param name="fromIndex">The element's position now.</param>
    /// <param name="toIndex">The position it should occupy afterwards.</param>
    /// <remarks>
    /// A collection with a move primitive of its own - <see cref="TextSpanCollection.Move"/> is the
    /// one this library has - keeps ownership and the inline control's place in the tree. A collection
    /// without one falls back to a remove and an insert, which is the same answer but with the churn
    /// the move primitive exists to avoid.
    /// </remarks>
    public abstract void Move(int fromIndex, int toIndex);

    /// <summary>Describes <paramref name="live"/> and the descriptions a render gave it.</summary>
    /// <typeparam name="TElement">The collection's element type, inferred from <paramref name="live"/>.</typeparam>
    /// <param name="live">The control's own collection.</param>
    /// <param name="elementType">What the description knows about the element type.</param>
    /// <param name="nodes">The descriptions, or null when the render assigned none.</param>
    /// <param name="move">
    /// The collection's own move primitive, or null to fall back to remove and insert. The generated
    /// call supplies this when the property's type has a public <c>Move(int, int)</c>; it is how a
    /// reorder avoids unparenting an inline control.
    /// </param>
    /// <returns>The descriptor.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="live"/> or <paramref name="elementType"/> is null.</exception>
    public static VNodeCollection Create<TElement>(
        IList<TElement> live,
        ElementType elementType,
        IReadOnlyList<VElementDescription>? nodes,
        Action<int, int>? move = null)
        where TElement : class
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(elementType);

        return new TypedCollection<TElement>(live, elementType, nodes, move);
    }

    private sealed class TypedCollection<TElement> : VNodeCollection
        where TElement : class
    {
        private readonly IList<TElement> _live;
        private readonly ElementType _elementType;
        private readonly IReadOnlyList<VElementDescription>? _nodes;
        private readonly Action<int, int>? _move;

        internal TypedCollection(
            IList<TElement> live,
            ElementType elementType,
            IReadOnlyList<VElementDescription>? nodes,
            Action<int, int>? move)
        {
            _live = live;
            _elementType = elementType;
            _nodes = nodes;
            _move = move;
        }

        public override ElementType ElementType => _elementType;

        public override IReadOnlyList<VElementDescription>? Nodes => _nodes;

        public override int Count => _live.Count;

        public override object At(int index) => _live[index]!;

        public override void RemoveAt(int index) => _live.RemoveAt(index);

        public override void Insert(int index, object element) => _live.Insert(index, (TElement)element);

        public override void Move(int fromIndex, int toIndex)
        {
            if (_move is not null)
            {
                _move(fromIndex, toIndex);
                return;
            }

            var element = _live[fromIndex];
            _live.RemoveAt(fromIndex);
            _live.Insert(toIndex, element);
        }
    }
}

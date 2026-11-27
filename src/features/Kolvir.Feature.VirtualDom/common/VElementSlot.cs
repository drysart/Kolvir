namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// One control-typed member of an element: either a control the element already holds, or one the
/// element's node makes and assigns.
/// </summary>
/// <remarks>
/// <para>
/// The element analogue of <see cref="VNodeSlot"/>. The control is a <see cref="VNode"/> because it
/// is made and patched by the ordinary control machinery; the element it belongs to is an
/// <see cref="object"/> because the descriptor has to serve one element type per instantiation
/// without the feature knowing which.
/// </para>
/// <para>
/// A creating slot carries the element's current value and an assignment delegate, so the reconciler
/// can take over a control the element already had instead of replacing it. A borrowed slot carries
/// the control the element made and keeps; it is filled in place and never placed.
/// </para>
/// </remarks>
public readonly struct VElementSlot
{
    private readonly VNode? _node;
    private readonly Control? _control;
    private readonly Func<object, Control?>? _current;
    private readonly Action<object, Control?>? _assign;

    private VElementSlot(VNode? node, Control? control, Func<object, Control?>? current, Action<object, Control?>? assign)
    {
        _node = node;
        _control = control;
        _current = current;
        _assign = assign;
    }

    /// <summary>The node for the slot this render, or null when the slot is empty.</summary>
    public VNode? Node => _node;

    /// <summary>The control the element already holds, for a borrowed slot; null for a creating slot.</summary>
    public Control? Control => _control;

    /// <summary>True for a slot whose control the element holds rather than one the node makes.</summary>
    public bool IsBorrowed => _assign is null;

    /// <summary>Reads the control the element currently holds, for a creating slot.</summary>
    /// <param name="element">The element to read.</param>
    /// <returns>The control, or null when the element has none.</returns>
    public Control? Current(object element) => _current?.Invoke(element);

    /// <summary>Assigns the control the reconciler made, or null to restore the element's own value.</summary>
    /// <param name="element">The element to assign.</param>
    /// <param name="control">The control, or null.</param>
    public void Assign(object element, Control? control) => _assign!(element, control);

    /// <summary>Describes a slot whose control the element already holds.</summary>
    /// <param name="control">The control the element holds.</param>
    /// <param name="node">The node for the slot, or null for an empty slot.</param>
    /// <returns>The slot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="control"/> is null.</exception>
    public static VElementSlot Borrowed(Control control, VNode? node)
    {
        ArgumentNullException.ThrowIfNull(control);

        return new VElementSlot(node, control, current: null, assign: null);
    }

    /// <summary>Describes a slot whose control the element's node makes and assigns.</summary>
    /// <param name="node">The node for the slot, or null for an empty slot.</param>
    /// <param name="current">Reads the control the element currently holds.</param>
    /// <param name="assign">Assigns the control the node made, or null to restore the element's own value.</param>
    /// <returns>The slot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="current"/> or <paramref name="assign"/> is null.</exception>
    public static VElementSlot Creating(
        VNode? node,
        Func<object, Control?> current,
        Action<object, Control?> assign)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(assign);

        return new VElementSlot(node, control: null, current, assign);
    }
}

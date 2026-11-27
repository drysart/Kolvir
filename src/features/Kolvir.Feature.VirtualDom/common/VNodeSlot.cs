using System.ComponentModel;

namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// One property slot of a node: either a control the node has to fill, or a control the node has to
/// make and hand to the property.
/// </summary>
/// <remarks>
/// <para>
/// This is the record a generated node returns from <see cref="VNode.GetSlots"/> - one per slot, in
/// the order the slots are declared - and it is the machinery behind slot properties, not something an
/// application writes. It is public because generated code is emitted into the <em>application's</em>
/// assembly.
/// </para>
/// <para>
/// The two shapes are the two kinds of control-typed property. A <see cref="Borrowed"/> slot is a
/// get-only property: the control made the instance and put it where it wants it, so the reconciler
/// applies and patches the node onto that instance and never creates or places anything. A
/// <see cref="Creating"/> slot is a get-plus-set property: when the node's control type differs from
/// the one already mounted, the reconciler makes the control, applies the node to it and assigns the
/// control to the property.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct VNodeSlot
{
    private VNodeSlot(VNode? node, Control? control, Action<Control, Control>? assign)
    {
        Node = node;
        Control = control;
        Assign = assign;
    }

    /// <summary>The node for the slot this render, or null when the slot is empty.</summary>
    public VNode? Node { get; }

    /// <summary>
    /// The control the property already holds, for a slot the node fills in place; null for a slot
    /// whose control the reconciler makes.
    /// </summary>
    public Control? Control { get; }

    /// <summary>
    /// Puts the control the reconciler made into the property, for a slot whose control the reconciler
    /// makes; null for a slot the node fills in place.
    /// </summary>
    /// <remarks>
    /// The owner control is the first argument because the property is on it. It is called once per
    /// control the reconciler makes, never for an unchanged node type - which is the create-slot half of
    /// "an unchanged value is not re-applied" - and never when a slot empties. The control is the
    /// <em>node's</em> control, so a node for a derived control assigns fine to a property declared as
    /// the base: the property takes a control, not a node.
    /// </remarks>
    public Action<Control, Control>? Assign { get; }

    /// <summary>Describes a slot whose control the property already holds.</summary>
    /// <param name="control">The control the property holds.</param>
    /// <param name="node">The node for the slot, or null for an empty slot.</param>
    /// <returns>The slot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="control"/> is null.</exception>
    public static VNodeSlot Borrowed(Control control, VNode? node)
    {
        ArgumentNullException.ThrowIfNull(control);

        return new VNodeSlot(node, control, assign: null);
    }

    /// <summary>Describes a slot whose control the reconciler makes and assigns.</summary>
    /// <param name="node">The node for the slot, or null for an empty slot.</param>
    /// <param name="assign">Puts the control the reconciler made into the property.</param>
    /// <returns>The slot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assign"/> is null.</exception>
    public static VNodeSlot Creating(VNode? node, Action<Control, Control> assign)
    {
        ArgumentNullException.ThrowIfNull(assign);

        return new VNodeSlot(node, control: null, assign);
    }
}

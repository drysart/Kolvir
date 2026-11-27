namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// A description of one element in a control's get-only element collection - a <see cref="TextSpan"/>
/// is the case this exists for.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not a <see cref="VNode"/>. A node makes and patches a <see cref="Control"/>:
/// it has a <see cref="NodeType"/>, an <c>ApplyTo(Control)</c>, a place in the parent's control tree
/// and a keyed slot in the reconciler's <see cref="Mounted"/> record. An element has none of that. It
/// is a value a control owns and mutates in place, so its description only has to say what the
/// element should look like; the reconciler that owns the collection patches the existing element
/// through the element's own public members.
/// </para>
/// <para>
/// The generated description class is source-generated, so every member is a direct assignment to
/// the element type rather than a name lookup or a delegate: the generator already has the symbols
/// when it emits, and a runtime descriptor layer would only turn that knowledge into data the image
/// pays for. The base class exists so the reconciler can hold descriptions from more than one
/// element type.
/// </para>
/// </remarks>
public abstract class VElementDescription
{
    /// <summary>
    /// Identifies this description among a collection's elements, so that a reorder moves the same
    /// element instead of replacing it.
    /// </summary>
    /// <remarks>
    /// Matching is by key first and by position second. A keyed description finds the element that
    /// had the same key on the previous render, wherever it has moved to; every other description is
    /// matched to the previous item at its ordinal. When the two disagree the key wins, and the
    /// element is moved to the keyed description's position.
    /// </remarks>
    public object? Key { get; init; }

    /// <summary>What the description knows about the element it makes.</summary>
    public abstract ElementType ElementType { get; }

    /// <summary>Configures a freshly created element from this description.</summary>
    /// <param name="element">The element to configure.</param>
    public abstract void ApplyTo(object element);

    /// <summary>Patches <paramref name="element"/> from <paramref name="previous"/>, assigning only what changed.</summary>
    /// <param name="previous">The description that produced <paramref name="element"/> last time.</param>
    /// <param name="element">The element to patch.</param>
    public abstract void PatchFrom(VElementDescription previous, object element);

    /// <summary>
    /// The control-typed members of the element, in declaration order, or null when it has none.
    /// </summary>
    /// <param name="element">The element to read a borrowed member from.</param>
    /// <returns>One descriptor per member, or null.</returns>
    public virtual VElementSlot[]? GetSlots(object element) => null;

    /// <summary>
    /// Detaches everything this description attached to <paramref name="element"/>.
    /// </summary>
    /// <param name="element">The element being dropped.</param>
    /// <remarks>
    /// Called when the reconciler removes an element from its collection. Property values are not
    /// reset: the element is leaving, and the only thing that would otherwise outlive it is a handler
    /// delegate the collection no longer owns.
    /// </remarks>
    public virtual void Detach(object element)
    {
    }
}

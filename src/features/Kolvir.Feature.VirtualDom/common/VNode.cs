using System.ComponentModel;

namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// A description of one control, which the reconciler turns into a real control and then patches.
/// </summary>
/// <remarks>
/// <para>
/// A node is built with an object initializer over a generated class, so each property it sets is a
/// property the control itself declares:
/// </para>
/// <code>
/// new VLabel { Text = "hello" }
/// new VStack { Gap = 8, Children = [ new VLabel { Text = "a" }, ..rows ] }
/// </code>
/// <para>
/// Which properties a node says something about is decided by which ones it assigns. A property that
/// is not assigned is not the same as one assigned <c>null</c>: the first says nothing and lets the
/// control's own value stand (or restores it, if a previous render had set it), while the second sets
/// the property to <c>null</c> and is only meaningful where the control's property is nullable. That
/// distinction is why the generated properties are tracked rather than plain auto-properties.
/// </para>
/// </remarks>
public abstract class VNode
{
    private Dictionary<object, object?>? _extensions;

    /// <summary>
    /// Identifies this node among its siblings, so that a reorder moves it instead of replacing it.
    /// </summary>
    /// <remarks>
    /// Reconciliation matches keyed children by key before it matches anything by position, which is
    /// what lets a list be reordered, inserted into or reversed without disturbing the controls that
    /// survived: a moved control keeps its scroll position, selection, caret and focus, because it was
    /// moved rather than rebuilt. Leave it null for a child whose position is its identity.
    /// </remarks>
    public object? Key { get; init; }

    /// <summary>The child nodes, in the order they should be laid out and painted.</summary>
    /// <remarks>
    /// Init-only rather than a get-only list so that a collection expression works, including spread:
    /// <c>Children = [ heading, ..rows ]</c>. The reconciler only looks at it for a control that can
    /// hold children; assigning it to a node whose control cannot is a mistake the generator reports.
    /// </remarks>
    public IReadOnlyList<VNode> Children { get; init; } = [];

    /// <summary>The generated description of the control this node makes.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public abstract NodeType NodeType { get; }

    /// <summary>Creates and configures the control for a node that has no previous control to reuse.</summary>
    /// <param name="control">The freshly created control.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public abstract void ApplyTo(Control control);

    /// <summary>
    /// Patches <paramref name="control"/> from <paramref name="previous"/>, assigning only what changed.
    /// </summary>
    /// <param name="previous">The node that produced <paramref name="control"/> on the previous render.</param>
    /// <param name="control">The control to patch.</param>
    /// <remarks>
    /// The previous node is the authority on what the control currently looks like - nothing reads a
    /// control's property back out - so a property present in the previous node and absent from this
    /// one is restored to its pristine value, and one that is unchanged is not assigned at all.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public abstract void PatchFrom(VNode previous, Control control);

    /// <summary>
    /// The property slots this node fills, in declaration order, or null when it has none.
    /// </summary>
    /// <param name="control">The control this node describes, which a borrowed slot's control is read from.</param>
    /// <returns>One descriptor per slot; null for every node class that declares none.</returns>
    /// <remarks>
    /// <para>
    /// A slot is a property whose type derives from <see cref="Control"/> and that a node fills with a
    /// node rather than a value - <c>SplitContainer.Panel1</c> is the one this library has. The order is
    /// the order the slots are declared in, which is fixed by the generated method that returns them:
    /// the reconciler pairs slot <em>n</em> of a render with slot <em>n</em> of the previous one, so two
    /// renders of the same node class must list their slots alike. They do, because both lists come
    /// from the same generated code.
    /// </para>
    /// <para>
    /// Public because generated code is emitted into the application's assembly; nothing outside the
    /// generated code and the reconciler should call it. A node class with no slots does not override
    /// it, so the whole mechanism costs an ordinary node one virtual call that returns null.
    /// </para>
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual VNodeSlot[]? GetSlots(Control control) => null;

    /// <summary>
    /// The get-only element collections this node describes, in declaration order, or null when it
    /// describes none.
    /// </summary>
    /// <param name="control">The control whose collections the descriptors wrap.</param>
    /// <returns>One descriptor per collection; null for every node class that declares none.</returns>
    /// <remarks>
    /// <para>
    /// A collection property is a get-only <see cref="IList{T}"/> of an element type the compilation
    /// marked with <c>[VirtualizableElement]</c> - <see cref="RichLabel.Spans"/> is the one this library
    /// has. The node carries a sequence of element descriptions and the reconciler reconciles them
    /// against the live elements in flow order, through the collection's own public mutators so that
    /// whatever the control does on add and remove still happens.
    /// </para>
    /// <para>
    /// Public because generated code is emitted into the application's assembly; nothing outside the
    /// generated code and the reconciler should call it. A node class with no collection does not
    /// override it, so the mechanism costs an ordinary node one virtual call that returns null.
    /// </para>
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual VNodeCollection[]? GetCollections(Control control) => null;

    /// <summary>
    /// A node of this class that assigns no property at all, or null when the class does not emit one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Patching a control from this node is how a borrowed slot hands its control back: every property
    /// the removed node assigned goes to that property's pristine value, every extension handler it
    /// attached is detached, and a property it never mentioned is not assigned at all - the same rule
    /// an ordinary render follows, with "this render says nothing" as the whole of the new node.
    /// </para>
    /// <para>
    /// It is a static instance of the node class, shared by every removal that class takes part in, and
    /// that is safe because it is only ever the <em>source</em> of a patch: nothing writes a node's
    /// properties outside its setters, and no setter ever runs on this one. The generated code emits it
    /// only for a node class whose control can appear in a slot, so a node that can never be one does
    /// not pay for it.
    /// </para>
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual VNode? UnassignedNode => null;

    /// <summary>
    /// The values this node carries for the extension handler members it has been given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An extension handler pair has no generated field on the node class, because naming the pair's
    /// accessor is what roots the pair in a published image. The value is kept here instead, keyed by
    /// the member's own identity token, and the member is installed the first time one of these
    /// properties is assigned.
    /// </para>
    /// <para>
    /// Public because generated code is emitted into the <em>application's</em> assembly, where this
    /// one's internals are not reachable; <see cref="EditorBrowsableAttribute"/> keeps it out of
    /// IntelliSense without changing accessibility, which works here precisely because the consumer is
    /// a different assembly. Nothing outside the generated code and
    /// <see cref="VNodeExtensionActuatorSet"/> should read or write it, and writing it directly is what
    /// the apply path reports as an invariant violation. The dictionary is not allocated until the
    /// node is actually given a handler.
    /// </para>
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Dictionary<object, object?> Extensions => _extensions ??= [];

    /// <summary>The extension values without creating the dictionary, for the apply path's fast case.</summary>
    internal Dictionary<object, object?>? ExtensionsOrNull => _extensions;

    /// <summary>
    /// Applies every installed extension handler member to <paramref name="control"/>.
    /// </summary>
    /// <param name="previous">The node that produced <paramref name="control"/> on the previous render, or null on a first apply.</param>
    /// <param name="control">The control being configured or patched.</param>
    /// <remarks>
    /// A generated node class makes this one call instead of a line per extension handler member, which
    /// is also what keeps the generated source - and the reference from the node to the member's
    /// accessor - out of the class. An apply for a node that has never been given a handler reaches no
    /// extension code at all, because no member is installed yet.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void ApplyExtensionActuators(VNode? previous, Control control) =>
        VNodeExtensionActuatorSet.Apply(previous, this, control);
}

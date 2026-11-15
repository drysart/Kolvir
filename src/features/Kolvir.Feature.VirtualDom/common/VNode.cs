using System.ComponentModel;

namespace Kolvir.VirtualDom;

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
}

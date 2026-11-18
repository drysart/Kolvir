using System.ComponentModel;

namespace Kolvir.VirtualDom;

/// <summary>
/// A component as a node, so a component can be a child of another component's render.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes a component a <em>child</em> rather than a value. The child keeps a mount of its
/// own, so its <see cref="Component.SetState"/> re-renders and patches only its own subtree, and a
/// render of the parent does not touch it: the child's controls keep their caret, their scroll
/// position and their focus while the parent's render count rises.
/// </para>
/// <para>
/// A node is matched against the previous render by its component <em>instance</em>. Emitting the same
/// component again keeps its mount and its controls; a different instance in the same slot disposes the
/// child that was there and mounts the new one. Give the node a <see cref="VNode.Key"/> when the list
/// can reorder, so a child that moves keeps its controls instead of being rebuilt in its new position.
/// </para>
/// <para>
/// The parent owns the child's <em>place</em> and the child owns its own content: the parent decides
/// that there is a child here and in what order, and the child decides what it looks like. That is the
/// division a component tree needs, and it is why a child's state is never part of the parent's.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Children = [.. _rows.Select(row => (VNode)new VComponent(row) { Key = row })],
/// </code>
/// </example>
public sealed class VComponent : VNode
{
    private const string NotAControl =
        "A VComponent is not a control node: the reconciler mounts the component and the component's own "
        + "render supplies the control. Reaching this means a node was treated as a control node.";

    /// <summary>Wraps <paramref name="component"/> so that it can be a child node.</summary>
    /// <param name="component">The component to mount under the parent that renders this node.</param>
    /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
    public VComponent(IComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        Component = component;
    }

    /// <summary>The component this node stands for.</summary>
    public IComponent Component { get; }

    /// <inheritdoc />
    /// <remarks>
    /// A component node makes no control of its own - the component's render does - so there is none to
    /// describe. Only the reconciler ever asks, and it mounts the component instead.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override NodeType NodeType => throw new InvalidOperationException(NotAControl);

    /// <inheritdoc />
    /// <remarks>Never called: a component node has no control to apply anything to.</remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override void ApplyTo(Control control) => throw new InvalidOperationException(NotAControl);

    /// <inheritdoc />
    /// <remarks>
    /// Never called: there is nothing to patch, because the child's own mount patches its subtree and
    /// the parent's render leaves it alone.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override void PatchFrom(VNode previous, Control control) =>
        throw new InvalidOperationException(NotAControl);
}

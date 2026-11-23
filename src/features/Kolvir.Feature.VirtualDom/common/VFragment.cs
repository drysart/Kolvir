using System.ComponentModel;

namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// A group of nodes with no control of its own, whose children take its place among the parent's children.
/// </summary>
/// <remarks>
/// <para>
/// A fragment is structure and nothing else. The reconciler creates no control for it, and its children
/// become children of the fragment's own parent, at the position the fragment occupies: <c>[a,
/// fragment([b, c]), d]</c> reconciles into exactly the controls <c>[a, b, c, d]</c> would, in the same
/// order, with the same patching across renders.
/// </para>
/// <para>
/// That makes it usable anywhere a single node is - including the return value of
/// <see cref="IComponent.Render"/> - in the two cases a bare node cannot cover. A render with several
/// top-level nodes to return has nowhere to put them without inventing a container control, which lays
/// out, paints and takes a slot in the z-order that the tree did not ask for. And a render with nothing
/// to return at all has no node to return: the empty fragment, <c>new VFragment()</c>, produces no
/// controls, so a screen with nothing to show yet occupies no space in its parent.
/// </para>
/// <code>
/// protected override VNode RenderWithDataModel(RenderArgs&lt;DocumentModel&gt; args) =&gt; new VFragment
/// {
///     Children =
///     [
///         new VLabel { Text = args.DataModel.Title },
///         new VLabel { Text = $"{args.DataModel.WordCount} words" },
///     ],
/// };
/// </code>
/// <para>
/// <b>A fragment is not a sibling.</b> Because its children are inlined into their parent, they are
/// matched as siblings of everything else in that parent: a key has to be unique among <em>all</em> of
/// them, not just among the children of one fragment. And a <see cref="VNode.Key"/> on the fragment
/// itself is refused rather than ignored, because inlining leaves nothing there for a key to identify;
/// key the children that move.
/// </para>
/// <para>
/// <b>Depth is bounded.</b> Fragments nest - a fragment's child may be another fragment - and inlining
/// is recursive, so a fragment that ends up containing itself as a descendant would never finish. That
/// is refused by name rather than left to the stack.
/// </para>
/// </remarks>
public sealed class VFragment : VNode
{
    private const string NotAControl =
        "A VFragment is not a control node: the reconciler inlines its children into the parent in its "
        + "place, so there is no control to describe, configure or patch. Reaching this means a node was "
        + "treated as a control node.";

    /// <inheritdoc />
    /// <remarks>
    /// A fragment makes no control - its children make theirs, and the parent makes the room - so there
    /// is none to describe. Only the reconciler would ever ask, and it inlines the fragment instead.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override NodeType NodeType => throw new InvalidOperationException(NotAControl);

    /// <inheritdoc />
    /// <remarks>Never called: an inlined fragment has no control to apply anything to.</remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override void ApplyTo(Control control) => throw new InvalidOperationException(NotAControl);

    /// <inheritdoc />
    /// <remarks>
    /// Never called: there is nothing to patch, because a fragment leaves no control behind. Its children
    /// are patched exactly as they would be if the parent had listed them itself.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override void PatchFrom(VNode previous, Control control) =>
        throw new InvalidOperationException(NotAControl);
}

namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// One node, the control it owns, and the mounted children underneath them both.
/// </summary>
/// <remarks>
/// This is the reconciler's record of what is currently on screen. It exists because the diff is
/// driven by the previous <em>node</em> rather than by the control: the node knows which properties
/// were assigned, which is what makes "restore this property" answerable without reading the control
/// back out.
/// </remarks>
internal sealed class Mounted
{
    private readonly Control? _control;

    internal Mounted(VNode node, Control? control)
    {
        Node = node;
        _control = control;
    }

    /// <summary>The node that produced <see cref="Control"/> on the latest render.</summary>
    internal VNode Node { get; set; }

    /// <summary>The control currently on screen for this node, or null when it stands for none.</summary>
    /// <remarks>
    /// <para>
    /// Read through the child's mount when there is one: a component may replace its own root control -
    /// a render that returns a different kind of node is a legal thing for it to do - and the parent's
    /// record has to follow, or the slot would keep pointing at a control that is no longer in the tree.
    /// </para>
    /// <para>
    /// Null is the empty case, and only a component reaches it: a component whose render is an empty
    /// <see cref="VFragment"/> is mounted, holds the child's state and re-renders, and occupies no slot
    /// in its parent until it has something to show.
    /// </para>
    /// </remarks>
    internal Control? Control => ComponentMount?.Root ?? _control;

    /// <summary>How many controls this record owns in its owner.</summary>
    /// <remarks>
    /// One for an ordinary node, and none for a component that renders nothing. A component that returns
    /// several top-level nodes - which a <see cref="VFragment"/> makes legal - owns one control per node,
    /// which is what lets the parent place the whole span without knowing what the child rendered.
    /// </remarks>
    internal int ControlCount => ComponentMount?.ControlCount ?? 1;

    /// <summary>The mounted children of <see cref="Control"/>, in control order.</summary>
    internal List<Mounted> Children { get; } = [];

    /// <summary>
    /// The mounted state of each property slot, by the slot's ordinal, or null when the node has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A slot entry is an ordinary <see cref="Mounted"/>, so a slot's patch, removal and nested subtree
    /// are the child machinery applied to one control: an empty slot is a null entry, and a filled one
    /// holds the node and the control it filled.
    /// </para>
    /// <para>
    /// The list is parallel to the node's slot list, which is why it can be indexed by ordinal: the
    /// slots a node lists come from its generated class, so two renders of the same class list them in
    /// the same order. It is allocated the first time a node with slots is mounted, and a node with no
    /// slots never allocates it.
    /// </para>
    /// </remarks>
    internal List<Mounted?>? Slots { get; set; }

    /// <summary>
    /// True for a slot whose control the node fills in place, so removal leaves the control where its
    /// owner put it instead of detaching it.
    /// </summary>
    internal bool Borrowed { get; init; }

    /// <summary>
    /// The child component's mount, for a node that is a <see cref="VComponent"/>, and null otherwise.
    /// </summary>
    /// <remarks>
    /// A component node's subtree belongs to this mount rather than to <see cref="Children"/>: the
    /// child renders and patches it, so the parent's render never walks it.
    /// </remarks>
    internal ComponentMount? ComponentMount { get; init; }
}

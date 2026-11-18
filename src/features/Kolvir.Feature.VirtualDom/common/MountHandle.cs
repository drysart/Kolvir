namespace Kolvir.VirtualDom;

/// <summary>
/// A mounted node tree: what <see cref="VirtualDomRuntime.Mount(Control, VNode)"/> hands back, and what a render updates.
/// </summary>
/// <remarks>
/// <para>
/// The handle owns the record of what is on screen, so it is the thing that knows which controls a node
/// became. Disposing it detaches everything, which is what a window closing should do.
/// </para>
/// <para>
/// It owns a <em>slice</em> of the parent's children rather than a single child: a root
/// <see cref="VFragment"/> puts one control per child of the fragment there, and the slice is everything
/// from the position it mounted at. A mount with nothing to show - a component whose render is an empty
/// fragment - owns no controls at all and still owns the place it will put them in.
/// </para>
/// </remarks>
public sealed class MountHandle : IDisposable
{
    private readonly Control _parent;
    private readonly List<Mounted> _roots = [];
    private int _startIndex;
    private bool _isMounted;

    internal MountHandle(Control parent, VNode root)
    {
        RejectComponentRoot(root);

        _parent = parent;
        Mount(root);
    }

    /// <summary>The first control the root node became, or null when the tree makes none.</summary>
    /// <remarks>
    /// A root fragment becomes as many controls as it has children, so there is no single control to
    /// name: this is the first of them, and null when the fragment is empty - which is also what a
    /// component that renders nothing reports here.
    /// </remarks>
    public Control? Root
    {
        get
        {
            foreach (var root in _roots)
            {
                if (root.Control is { } control)
                {
                    return control;
                }
            }

            return null;
        }
    }

    /// <summary>True while a tree is mounted, whether or not it made any controls.</summary>
    /// <remarks>
    /// A tree that renders nothing is mounted in every sense that matters: it holds its place in the
    /// parent, it listens to what its render reads, and its next render puts controls there. Only
    /// <see cref="Root"/> is null.
    /// </remarks>
    public bool IsMounted => _isMounted;

    /// <summary>How many controls this tree owns in its parent.</summary>
    internal int ControlCount
    {
        get
        {
            var count = 0;

            foreach (var root in _roots)
            {
                count += root.ControlCount;
            }

            return count;
        }
    }

    /// <summary>
    /// Reconciles <paramref name="root"/> into what is already mounted, patching controls where it can.
    /// </summary>
    /// <param name="root">The tree to show now.</param>
    /// <remarks>
    /// A control is reused whenever the new root node is the same kind as the old one, so scroll
    /// position, selection, caret and focus survive a render that only changed a value. When the root
    /// node is a different kind, the old control is replaced in place: the new one takes the slot the old
    /// one had, and any sibling the parent had around it stays where it is.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is null.</exception>
    public void Update(VNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        RejectComponentRoot(root);

        if (!_isMounted)
        {
            Mount(root);
            return;
        }

        // The ordinary case, and the one that has to stay cheap: one control, patched where it sits.
        if (_roots.Count == 1 && root is not VFragment)
        {
            var mounted = _roots[0];

            if (mounted.ComponentMount is null
                && mounted.Control is { } control
                && ReferenceEquals(mounted.Node.NodeType, root.NodeType))
            {
                root.PatchFrom(mounted.Node, control);
                Reconciler.ReconcileChildren(control, mounted.Children, root.Children);
                mounted.Node = root;
                return;
            }
        }

        // Anything else - a different kind of root, a fragment, or a root that makes no controls - is
        // reconciled as what it is: the slice of the parent's children this handle owns.
        Reconciler.ReconcileChildren(_parent, _roots, [root], _startIndex);
    }

    /// <summary>Detaches the mounted tree: every control is removed and its handlers die with it.</summary>
    /// <remarks>
    /// The handle stays usable afterwards; a later <see cref="Update"/> mounts from scratch, at the end of
    /// the parent's children rather than at the position this tree used to hold.
    /// </remarks>
    public void Unmount()
    {
        if (!_isMounted)
        {
            return;
        }

        foreach (var root in _roots)
        {
            Reconciler.Unmount(root);
        }

        _roots.Clear();
        _isMounted = false;
    }

    /// <inheritdoc />
    public void Dispose() => Unmount();

    /// <summary>Moves this tree's controls so that they start at <paramref name="index"/>.</summary>
    /// <param name="index">The position in the parent's children the first control belongs at.</param>
    /// <remarks>
    /// Called by the reconciler when the parent moves this mount into place. It has to work with an empty
    /// tree as well as a full one: the position is remembered, so the controls a later render creates are
    /// put where the parent said this child is.
    /// </remarks>
    internal void PlaceIn(int index)
    {
        _startIndex = index;

        var at = index;

        foreach (var root in _roots)
        {
            Reconciler.Place(_parent, root, at);
            at += root.ControlCount;
        }
    }

    private void Mount(VNode root)
    {
        _startIndex = _parent.Controls.Count;
        _isMounted = true;
        Reconciler.ReconcileChildren(_parent, _roots, [root], _startIndex);
    }

    /// <summary>
    /// Refuses a component node as the root of a handle.
    /// </summary>
    /// <param name="root">The tree being mounted.</param>
    /// <remarks>
    /// A handle owns the place a tree's controls sit in, and patches that tree node by node, which a
    /// component node does not have: the component's render supplies the controls and the component's own
    /// mount keeps them in step. A root component is therefore mounted as a component, and the message
    /// says so rather than leaving the caller to work out why a control did not appear.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="root"/> is a <see cref="VComponent"/>.</exception>
    private static void RejectComponentRoot(VNode root)
    {
        if (root is VComponent)
        {
            throw new ArgumentException(
                "A VComponent cannot be the root of a MountHandle. Mount the component itself with VirtualDomRuntime.Mount(parent, component), which returns the ComponentMount that keeps it in step; a VComponent is for a child of another component's render.",
                nameof(root));
        }
    }
}

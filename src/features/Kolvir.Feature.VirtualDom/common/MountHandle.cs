namespace Kolvir.VirtualDom;

/// <summary>
/// A mounted node tree: what <see cref="VirtualDomRuntime.Mount(Control, VNode)"/> hands back, and what a render updates.
/// </summary>
/// <remarks>
/// The handle owns the record of what is on screen, so it is the thing that knows which control a
/// node became. Disposing it detaches everything, which is what a window closing should do.
/// </remarks>
public sealed class MountHandle : IDisposable
{
    private readonly Control _parent;
    private Mounted? _root;

    internal MountHandle(Control parent, VNode root)
    {
        RejectComponentRoot(root);

        _parent = parent;
        _root = MountParent(root);
    }

    /// <summary>The control the root node became, or null when nothing is mounted.</summary>
    public Control? Root => _root?.Control;

    /// <summary>True while a tree is mounted.</summary>
    public bool IsMounted => _root is not null;

    /// <summary>
    /// Reconciles <paramref name="root"/> into what is already mounted, patching controls where it can.
    /// </summary>
    /// <param name="root">The tree to show now.</param>
    /// <remarks>
    /// A control is reused whenever the new root node is the same kind as the old one, so scroll
    /// position, selection, caret and focus survive a render that only changed a value. When the root
    /// node is a different kind, the old control is replaced in place.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is null.</exception>
    public void Update(VNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        RejectComponentRoot(root);

        if (_root is null)
        {
            _root = MountParent(root);
            return;
        }

        if (!ReferenceEquals(_root.Node.NodeType, root.NodeType))
        {
            // A different root node class cannot be patched, so the control is swapped where it sits.
            var index = _parent.Controls.IndexOf(_root.Control);
            Reconciler.Unmount(_root);
            _root = MountParent(root, index < 0 ? _parent.Controls.Count : index);
            return;
        }

        root.PatchFrom(_root.Node, _root.Control);
        Reconciler.ReconcileChildren(_root.Control, _root.Children, root.Children);
        _root.Node = root;
    }

    /// <summary>Detaches the mounted tree: every control is removed and its handlers die with it.</summary>
    /// <remarks>
    /// The handle stays usable afterwards; a later <see cref="Update"/> mounts from scratch.
    /// </remarks>
    public void Unmount()
    {
        if (_root is null)
        {
            return;
        }

        Reconciler.Unmount(_root);
        _root = null;
    }

    /// <inheritdoc />
    public void Dispose() => Unmount();

    /// <summary>
    /// Refuses a component node as the root of a handle.
    /// </summary>
    /// <param name="root">The tree being mounted.</param>
    /// <remarks>
    /// A handle owns one root control and patches it node by node, which a component node does not have:
    /// the component's render supplies the control and the component's own mount keeps it in step. A
    /// root component is therefore mounted as a component, and the message says so rather than leaving
    /// the caller to work out why a control did not appear.
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

    private Mounted MountParent(VNode root, int index = -1)
    {
        var mounted = Reconciler.Create(_parent, root);

        if (index < 0 || index > _parent.Controls.Count)
        {
            _parent.Controls.Add(mounted.Control);
        }
        else
        {
            _parent.Controls.Insert(index, mounted.Control);
        }

        Reconciler.ReconcileChildren(mounted.Control, mounted.Children, root.Children);
        return mounted;
    }
}

namespace Kolvir.VirtualDom;

/// <summary>
/// A mounted node tree: what <see cref="VirtualDomRuntime.Mount"/> hands back, and what a render updates.
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

    private Mounted MountParent(VNode root, int index = -1)
    {
        var mounted = Reconciler.Create(root);

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

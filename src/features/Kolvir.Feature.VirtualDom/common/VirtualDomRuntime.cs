namespace Kolvir.VirtualDom;

/// <summary>
/// Turns a node tree into controls under a container, and keeps it in step as renders change it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Mount"/> is the whole entry point: give it a control to fill and the tree to put there,
/// and it returns a handle whose <see cref="MountHandle.Update"/> is what a render calls. That is
/// deliberately the entire API, because a virtual DOM is a way of owning a subtree, not a control.
/// </para>
/// <para>
/// The component layer (view models whose <c>Render</c> returns a <see cref="VNode"/>) is built on top
/// of this and is usable without it, which is what keeps the reconciler testable with no state
/// machinery in the way.
/// </para>
/// </remarks>
public static class VirtualDomRuntime
{
    /// <summary>Mounts <paramref name="root"/> under <paramref name="parent"/>.</summary>
    /// <param name="parent">The control whose children the tree owns. Typically a window's root panel.</param>
    /// <param name="root">The tree to show.</param>
    /// <returns>A handle that patches the mounted tree when it is updated.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="parent"/> or <paramref name="root"/> is null.
    /// </exception>
    public static MountHandle Mount(Control parent, VNode root)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(root);

        return new MountHandle(parent, root);
    }
}

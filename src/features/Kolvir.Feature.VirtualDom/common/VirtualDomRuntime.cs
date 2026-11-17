namespace Kolvir.VirtualDom;

/// <summary>
/// Turns a node tree into controls under a container, and keeps it in step as renders change it.
/// </summary>
/// <remarks>
/// <para>
/// There are two entry points and nothing else. <see cref="Mount(Control, VNode)"/> takes a tree that
/// the application rebuilds and compares itself, and hands back a handle whose
/// <see cref="MountHandle.Update"/> is what a render calls. <see cref="Mount(Control, Component)"/>
/// takes a view model that renders itself, which is the same thing with the comparison and the
/// scheduling wrapped up. That is deliberately the entire API, because a virtual DOM is a way of
/// owning a subtree, not a control.
/// </para>
/// <para>
/// The component layer is built on top of the tree API and the tree API does not know about it, which
/// is what keeps the reconciler testable with no state machinery in the way.
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

    /// <summary>Mounts <paramref name="component"/>'s tree under <paramref name="parent"/>.</summary>
    /// <param name="parent">The control whose children the tree owns. Typically a window's root panel.</param>
    /// <param name="component">The component to render and keep in step.</param>
    /// <returns>
    /// A handle that re-renders the component whenever <see cref="Component.SetState"/> is called, which
    /// also detaches the tree when it is disposed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="parent"/> or <paramref name="component"/> is null.
    /// </exception>
    /// <example>
    /// <code>
    /// private ComponentMount? _screen;
    ///
    /// void Show() => _screen = VirtualDomRuntime.Mount(Root, new MainScreen());
    /// </code>
    /// </example>
    public static ComponentMount Mount(Control parent, Component component)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(component);

        return new ComponentMount(parent, component);
    }
}

namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// Turns a node tree into controls under a container, and keeps it in step as renders change it.
/// </summary>
/// <remarks>
/// <para>
/// There are three entry points and nothing else. <see cref="Mount(Control, VNode)"/> takes a tree
/// that the application rebuilds and compares itself, and hands back a handle whose
/// <see cref="MountHandle.Update"/> is what a render calls. <see cref="Mount(Control, IComponent)"/>
/// takes a view model that renders itself, which is the same thing with the comparison and the
/// scheduling wrapped up. <see cref="Mount(Application, IApplicationComponent)"/> takes that idea one
/// level up, so a component owns an application's windows and each window's content is still an
/// ordinary control mount. That is deliberately the entire API, because a virtual DOM is a way of
/// owning a subtree or a window set, not a control.
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
    /// A handle that re-renders the component whenever <see cref="ComponentMount.Invalidate"/> is called - which
    /// is what <see cref="Component.SetState"/> does - and that also detaches the tree when it is disposed.
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
    public static ComponentMount Mount(Control parent, IComponent component)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(component);

        return new ComponentMount(parent, component);
    }

    /// <summary>
    /// Mounts <paramref name="component"/>'s windows into <paramref name="application"/>.
    /// </summary>
    /// <param name="application">The application the windows belong to. It has to be the current one.</param>
    /// <param name="component">The application component to render and keep in step.</param>
    /// <returns>
    /// A handle that owns the window set, re-renders the component whenever
    /// <see cref="ApplicationMount.Invalidate"/> is called, and closes the windows when it is
    /// disposed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="application"/> or <paramref name="component"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="application"/> is not the current application, or the component's render returns
    /// something other than windows.
    /// </exception>
    /// <remarks>
    /// The application-level entry point. The first render happens here, synchronously, so an
    /// application that has not entered <see cref="Application.Run()"/> still has its windows
    /// registered and ready to show; every later render waits for a frame. Each window's
    /// <see cref="VWindow.Content"/> is still an ordinary control tree, including a bare
    /// <see cref="VComponent"/> root.
    /// </remarks>
    /// <example>
    /// <code>
    /// using var application = new Application(KolvirBackend.CreateDefault(), options);
    /// using var windows = VirtualDomRuntime.Mount(application, new Shell());
    /// return application.Run();
    /// </code>
    /// </example>
    public static ApplicationMount Mount(Application application, IApplicationComponent component)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(component);

        return new ApplicationMount(application, component);
    }
}

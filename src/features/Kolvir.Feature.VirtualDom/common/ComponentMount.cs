namespace Kolvir.VirtualDom;

/// <summary>
/// One component's rendered tree, kept in step with the controls under a mount point.
/// </summary>
/// <remarks>
/// <para>
/// The handle is what a window owns: disposing it detaches every control the tree made, which is what
/// closing a window does. It sits above <see cref="MountHandle"/> rather than replacing it, because
/// that handle is the reconciler's own entry point and stays usable without any component.
/// </para>
/// <para>
/// Holding the component rather than a node is the whole difference: the mount can render again on its
/// own, which is what makes <see cref="Component.SetState"/> a one-line call at a call site that knows
/// nothing about trees.
/// </para>
/// </remarks>
public sealed class ComponentMount : IDisposable
{
    private const int MaxRenderPasses = 100;

    private readonly MountHandle _handle;
    private bool _painting;
    private bool _dirty;
    private bool _disposed;

    /// <summary>Mounts <paramref name="component"/>'s first tree under <paramref name="parent"/>.</summary>
    /// <param name="parent">The control whose children the tree owns.</param>
    /// <param name="component">The component to render and keep in step.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="parent"/> or <paramref name="component"/> is null.
    /// </exception>
    internal ComponentMount(Control parent, Component component)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(component);

        Component = component;
        component.Attach(this);

        // The first render happens before there is a handle to patch, so a SetState from inside it has
        // to fold into this pass rather than looking for one. Rendering until clean is what turns that
        // into the same loop every later render runs.
        _handle = new MountHandle(parent, RenderUntilClean());
    }

    /// <summary>The component this mount renders.</summary>
    public Component Component { get; }

    /// <summary>The control the root node became, or null when nothing is mounted.</summary>
    public Control? Root => _handle.Root;

    /// <summary>True while a tree is mounted.</summary>
    public bool IsMounted => _handle.IsMounted;

    /// <inheritdoc />
    /// <remarks>
    /// Detaching is final for this mount. A later <see cref="Component.SetState"/> on the component is
    /// a no-op rather than an error: an event that arrives after its window closed is ordinary, and
    /// throwing there would make every teardown order-dependent.
    /// </remarks>
    public void Dispose()
    {
        _disposed = true;
        _handle.Dispose();
    }

    /// <summary>Re-renders and patches, or notes that the pass in progress has to run again.</summary>
    /// <remarks>
    /// A render that changes state calls back into here, and re-entering the reconciler in the middle
    /// of a diff would be undefined. So the request is recorded instead and the pass that is already
    /// running repeats, which is what lets a component correct itself during a render.
    /// </remarks>
    internal void Invalidate()
    {
        if (_disposed)
        {
            return;
        }

        if (_painting)
        {
            _dirty = true;
            return;
        }

        _handle.Update(RenderUntilClean());
    }

    private VNode RenderUntilClean()
    {
        VNode node;

        _painting = true;

        try
        {
            var passes = 0;

            do
            {
                _dirty = false;
                node = Component.Render();

                // A render that sets state every time never settles. Failing on the hundredth pass
                // names the component instead of hanging the application, which is the difference
                // between a stack trace and a frozen window.
                if (++passes > MaxRenderPasses)
                {
                    throw new InvalidOperationException(
                        $"{Component.GetType().Name} still had state to change after {MaxRenderPasses} renders. A render must not call SetState unconditionally.");
                }
            }
            while (_dirty);
        }
        finally
        {
            _painting = false;
        }

        return node;
    }
}

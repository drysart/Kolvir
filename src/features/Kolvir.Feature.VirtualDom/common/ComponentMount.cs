namespace Kolvir.Feature.VirtualDom;

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
/// nothing about trees. That render does not happen at the call site, though - it happens at the start
/// of the next frame, which is what <see cref="RenderQueue"/> is for.
/// </para>
/// </remarks>
public sealed class ComponentMount : IDisposable
{
    private readonly MountHandle _handle;
    private bool _pending;
    private bool _rendering;
    private bool _disposed;

    /// <summary>Mounts <paramref name="component"/>'s first tree under <paramref name="parent"/>.</summary>
    /// <param name="parent">The control whose children the tree owns.</param>
    /// <param name="component">The component to render and keep in step.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="parent"/> or <paramref name="component"/> is null.
    /// </exception>
    /// <remarks>
    /// The first render is the one that is not deferred: the tree has to exist before a frame can be asked
    /// to update it, so it renders here and every later render waits for a frame. It may render nothing -
    /// an empty <see cref="VFragment"/> - which leaves the mount owning a place in the parent and no
    /// controls in it.
    /// </remarks>
    internal ComponentMount(Control parent, IComponent component)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(component);

        Component = component;
        component.Attach(this);

        try
        {
            _handle = new MountHandle(parent, Render());
        }
        catch
        {
            // A mount that never happened must not leave the component pointing at a half-built handle:
            // every later SetState, and any attempt to mount it again, would dereference null. Render
            // has to run after the attach for a render that changes state to be caught by name, so the
            // attach is undone here rather than moved.
            component.Detach(this);
            throw;
        }
    }

    /// <summary>The component this mount renders.</summary>
    public IComponent Component { get; }

    /// <summary>The first control the root node became, or null when the render makes none.</summary>
    /// <remarks>
    /// A render that returns a <see cref="VFragment"/> makes as many controls as the fragment has
    /// children, so this is the first of them; a render that returns an empty one makes none, and the
    /// component is still mounted - it just has nothing on screen yet.
    /// </remarks>
    public Control? Root => _handle.Root;

    /// <summary>True while a tree is mounted, whether or not it made any controls.</summary>
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
        RenderQueue.Forget(this);
        _handle.Dispose();
        Component.Detach(this);
    }

    /// <summary>Asks for this component to be rendered on the next frame.</summary>
    /// <remarks>
    /// <para>
    /// The primitive a component is built on. <see cref="Component.SetState"/> is one line over it, and
    /// an <see cref="IComponent"/> of one's own calls it from its own state setter - which is why it is
    /// public: an interface whose only way to be re-rendered is a method the mount never calls would be
    /// a seam nothing can use.
    /// </para>
    /// <para>
    /// The whole of it is cheap on purpose: a flag, a queue entry and a frame request. Calling it twice
    /// for one frame queues the component once, which is what turns a handler that changes a list five
    /// times into one render and one tree update.
    /// </para>
    /// <para>
    /// Calling it from inside a render is refused rather than folded into the pass: a render describes
    /// what the state is, so a render that changes the state describes a tree that is already out of
    /// date, and there is no fixed point to iterate to. The message names the component.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The component changed state from inside its own <see cref="IComponent.Render"/>.
    /// </exception>
    public void Invalidate()
    {
        if (_disposed)
        {
            return;
        }

        if (_rendering)
        {
            throw new InvalidOperationException(
                $"{Component.GetType().Name} changed state from inside Render. A render describes the state it is given, so it must not call SetState; do it from the event handler that changed the state instead.");
        }

        if (_pending)
        {
            // Already queued for the next frame, and the frame has not run yet: the render that is
            // coming will read the state as it is by then.
            return;
        }

        _pending = true;
        RenderQueue.Enqueue(this);

        // Ask for the frame the render is waiting for. A control with no host asks for nothing, which
        // is right: there is no window to draw into yet, and the next frame of any window drains this.
        Root?.Invalidate();
    }

    /// <summary>How many controls this component's tree owns in its parent.</summary>
    internal int ControlCount => _handle.ControlCount;

    /// <summary>Moves this component's controls so that they start at <paramref name="index"/>.</summary>
    /// <param name="index">The position in the parent's children the first control belongs at.</param>
    /// <remarks>
    /// The parent places a child by asking it, rather than by moving a control it assumes exists: a
    /// component's render decides how many controls there are, and only the mount knows.
    /// </remarks>
    internal void PlaceIn(int index) => _handle.PlaceIn(index);

    /// <summary>Renders the component now, because the frame it was queued for has started.</summary>
    internal void RenderQueued()
    {
        if (_disposed || !_pending)
        {
            return;
        }

        _pending = false;
        _handle.Update(Render());
    }

    private VNode Render()
    {
        _rendering = true;

        try
        {
            return Component.Render();
        }
        finally
        {
            _rendering = false;
        }
    }
}

namespace Kolvir.VirtualDom;

/// <summary>
/// A view model whose <see cref="Render"/> describes the controls to show, and which re-renders itself
/// when its state changes.
/// </summary>
/// <remarks>
/// <para>
/// This is the optional half of the feature: a component is a thin wrapper over a mounted node tree.
/// Everything below it - <see cref="VNode"/>, the reconciler, <see cref="MountHandle"/> - works without
/// any component at all, and a view that prefers its own scheduling can keep using those directly.
/// </para>
/// <para>
/// The loop a component runs is: something changes state, <see cref="SetState"/> is called, and by the
/// time the next frame is drawn the component's <see cref="Render"/> has produced a new tree and the
/// mount has diffed it into the controls that are already there. A control the new tree still describes
/// is patched rather than rebuilt, so scroll position, selection, caret and focus survive a render that
/// only changed a value.
/// </para>
/// <para>
/// <see cref="SetState"/> is deliberately not a render. It marks the component and asks for a frame, so
/// a handler that changes a list five times renders once and touches the tree once, and no caller has
/// to think about layout thrashing. The renders themselves run in a batch at the start of the next
/// frame any window draws, in the order the state changed.
/// </para>
/// <para>
/// A component is re-rendered through the mount that owns its tree, which is what
/// <see cref="VirtualDomRuntime.Mount(Control, IComponent)"/> creates. Until something mounts it there is
/// no tree to patch, so <see cref="SetState"/> does nothing - and nothing is lost, because the first
/// render reads whatever the state is by then. A component that is never mounted shows no controls at
/// all, which is what tells its author that the mount call is missing. Once the mount is disposed,
/// <see cref="SetState"/> does nothing too, because there is nothing left to show and an event handler
/// that arrives after a window closes is not a mistake.
/// </para>
/// <para>
/// A component can also be a child of another component's render, by being wrapped in a
/// <see cref="VComponent"/> node. The child keeps a mount of its own, so its state changes patch its own
/// subtree and a render of its parent leaves it alone.
/// </para>
/// <para>
/// <see cref="Render"/> is a description and not a mutation, and that includes the state it reads: a
/// render must not call <see cref="SetState"/>. That contract is what lets a render be one pass with no
/// settling loop, and calling <see cref="SetState"/> from inside one is refused rather than deferred,
/// because a render that changes the state has already described a tree that is out of date.
/// </para>
/// <para>
/// The mount needs a smaller set of members than this class has - <see cref="Render"/> and the two
/// bookkeeping calls - and those are <see cref="IComponent"/>. Deriving from this class is what buys
/// <see cref="SetState"/> and the single-instance rule; a view model that cannot inherit, or that wants
/// to schedule its own way, implements the interface instead and calls
/// <see cref="ComponentMount.Invalidate"/> where this class calls it from <see cref="SetState"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// internal sealed class Counter : Component
/// {
///     private int _count;
///
///     public override VNode Render() => new VPanel
///     {
///         Dock = DockStyle.Fill,
///         Children =
///         [
///             new VLabel { Text = $"{_count} clicks" },
///             new VButton { Text = "Add", Click = () => { _count++; SetState(); } },
///         ],
///     };
/// }
///
/// using var mount = VirtualDomRuntime.Mount(window.Root, new Counter());
/// </code>
/// </example>
public abstract class Component : IComponent
{
    private ComponentMount? _mount;

    /// <summary>Describes the controls this component's current state calls for.</summary>
    /// <returns>The tree to reconcile into what is mounted.</returns>
    /// <remarks>
    /// Called by the mount, once per frame in which the state changed - not once per property. It is a
    /// description and not a mutation: build and return a tree, and let the reconciler work out what
    /// changed. It must not change state either: calling <see cref="SetState"/> from inside a render is
    /// refused, because a render that changes the state describes a tree that is already out of date.
    /// </remarks>
    public abstract VNode Render();

    /// <summary>Marks this component for a render, on the next frame.</summary>
    /// <remarks>
    /// <para>
    /// The whole of the protocol: assign the field the render reads, call this, and the screen follows
    /// on the next frame. It is cheap on purpose - a flag, a queue entry and a request for a frame - so
    /// a handler can call it as often as it likes, or several times as it changes several things, and
    /// the component still renders once.
    /// </para>
    /// <para>
    /// It does nothing when the component has no live tree - before the first mount, or after the mount
    /// was disposed. Nothing is lost in the first case, because the first render reads the state as it
    /// is by then; in the second there is no screen left to correct.
    /// </para>
    /// </remarks>
    public void SetState() => _mount?.Invalidate();

    /// <summary>Records the mount that owns this component's tree.</summary>
    /// <param name="mount">The mount that is about to render it.</param>
    /// <exception cref="InvalidOperationException">
    /// The component is already mounted. One component is one subtree, so a second mount would leave the
    /// first one rendering a component that no longer points at it.
    /// </exception>
    /// <remarks>
    /// Implemented explicitly, so it belongs to <see cref="IComponent"/> rather than to this class's own
    /// surface: an application has no reason to call it and no way to reach it.
    /// </remarks>
    void IComponent.Attach(ComponentMount mount)
    {
        if (_mount is { IsMounted: true } existing && !ReferenceEquals(existing, mount))
        {
            throw new InvalidOperationException(
                $"{GetType().Name} is already mounted. One component is one subtree, so it cannot appear twice in a tree; give each place its own component instance.");
        }

        _mount = mount;
    }

    /// <summary>Forgets <paramref name="mount"/> if it is the one this component renders through.</summary>
    /// <param name="mount">The mount being disposed.</param>
    /// <remarks>Explicit for the same reason as <see cref="IComponent.Attach"/>.</remarks>
    void IComponent.Detach(ComponentMount mount)
    {
        if (ReferenceEquals(_mount, mount))
        {
            _mount = null;
        }
    }
}

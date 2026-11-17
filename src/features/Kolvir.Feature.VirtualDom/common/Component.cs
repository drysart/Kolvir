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
/// The loop a component runs is: something changes state, <see cref="SetState"/> is called, the
/// component's <see cref="Render"/> produces a new tree, and the mount diffs it into the controls that
/// are already there. A control the new tree still describes is patched rather than rebuilt, so scroll
/// position, selection, caret and focus survive a render that only changed a value.
/// </para>
/// <para>
/// A component is re-rendered through the mount that owns its tree, which is what
/// <see cref="VirtualDomRuntime.Mount(Control, Component)"/> creates. A component that nothing has
/// mounted has no tree to patch, so <see cref="SetState"/> reports that rather than silently doing
/// nothing: a view model whose state changes and whose screen does not is the hardest kind of bug to
/// find. Once the mount is disposed, <see cref="SetState"/> is a no-op, because there is nothing left
/// to show and an event handler that arrives after a window closes is not a mistake.
/// </para>
/// <para>
/// Rendering is synchronous on the calling thread, which is the UI thread. There is no batching, no
/// scheduler and no background render: a <see cref="SetState"/> finishes with the controls on screen
/// already correct, which is what makes a test - and a developer - able to reason about it without a
/// frame clock.
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
public abstract class Component
{
    private ComponentMount? _mount;

    /// <summary>Describes the controls this component's current state calls for.</summary>
    /// <returns>The tree to reconcile into what is mounted.</returns>
    /// <remarks>
    /// Called by the mount, every time the component is rendered - not once per property. It is a
    /// description and not a mutation: build and return a tree, and let the reconciler work out what
    /// changed. Reading the state here is the whole of the read side, and calling
    /// <see cref="SetState"/> from inside a render is folded into the same pass rather than recursing.
    /// </remarks>
    public abstract VNode Render();

    /// <summary>Re-renders and patches the mounted tree, because this component's state changed.</summary>
    /// <remarks>
    /// Synchronous: when this returns, the controls on screen reflect the new state. It is what an
    /// event handler calls after assigning the field the render reads, and that is the whole protocol -
    /// there is no observable, no notification and no binding to keep in step.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Nothing has mounted this component, so it has no tree to patch. Message says what to do about it.
    /// </exception>
    public void SetState()
    {
        if (_mount is not { } mount)
        {
            throw new InvalidOperationException(
                $"This {GetType().Name} is not mounted. A component renders and patches the tree its mount owns, so it has to be handed to VirtualDomRuntime.Mount(parent, component) before its state changes.");
        }

        mount.Invalidate();
    }

    /// <summary>Records the mount that owns this component's tree.</summary>
    /// <param name="mount">The mount that is about to render it.</param>
    internal void Attach(ComponentMount mount) => _mount = mount;
}

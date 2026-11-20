namespace Kolvir.VirtualDom;

/// <summary>What an <see cref="ApplicationMount"/> needs from the component it renders.</summary>
/// <remarks>
/// <para>
/// The application-level counterpart of <see cref="IComponent"/>: describe the windows, and be told
/// which mount owns them and when that stops being true. It is the mount's half of the protocol and
/// only that half, so a base class of one's own implements these three members and gets the mount,
/// the deferred render, and the window patching with it.
/// </para>
/// <para>
/// <see cref="ApplicationComponent.Invalidate"/> is deliberately not here. It is the
/// application-facing half - the call an event handler makes - and the mount never calls it, so
/// requiring it would force every implementation to reproduce a spelling the mount has no opinion
/// about. What the mount does need is the primitive underneath it,
/// <see cref="ApplicationMount.Invalidate"/>, which is public for exactly this reason.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// internal sealed class Shell : IApplicationComponent
/// {
///     private ApplicationMount? _mount;
///
///     public IReadOnlyList&lt;ApplicationVNode&gt; Render() =&gt;
///         [new VWindow { Title = "Hello", Content = new VLabel { Text = "World" } }];
///
///     public void Attach(ApplicationMount mount) =&gt; _mount = mount;
///
///     public void Detach(ApplicationMount mount)
///     {
///         if (ReferenceEquals(_mount, mount))
///         {
///             _mount = null;
///         }
///     }
/// }
/// </code>
/// </example>
public interface IApplicationComponent
{
    /// <summary>Describes the windows this component's current state calls for.</summary>
    /// <returns>The window descriptors to reconcile into the real windows.</returns>
    /// <remarks>
    /// Called by the mount, once per frame in which the state changed - not once per property. It is
    /// a description and not a mutation: build and return the list, and let the reconciler work out
    /// what changed. It must not change state either: calling
    /// <see cref="ApplicationMount.Invalidate"/> from inside a render is refused, because a render
    /// that changes the state describes a window set that is already out of date.
    /// </remarks>
    IReadOnlyList<ApplicationVNode> Render();

    /// <summary>Records the mount that owns this component's windows.</summary>
    /// <param name="mount">The mount that is about to render it.</param>
    /// <remarks>
    /// Called once, before the first render, so that a state change during construction has a mount
    /// to reach. An implementation should refuse a second live mount: one component is one
    /// application, and a second mount would leave the first one rendering a component that no longer
    /// points at it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The component is already mounted.</exception>
    void Attach(ApplicationMount mount);

    /// <summary>Forgets <paramref name="mount"/> if it is the one this component renders through.</summary>
    /// <param name="mount">The mount being disposed, or the one whose first render threw.</param>
    /// <remarks>
    /// Called when the mount goes away, and the component is usually thrown away with it - but not
    /// always: a component can be mounted again elsewhere, and one whose first render threw is still
    /// perfectly usable. So this is a reset rather than a tombstone.
    /// </remarks>
    void Detach(ApplicationMount mount);
}

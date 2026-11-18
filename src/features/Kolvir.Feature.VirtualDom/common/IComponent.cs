namespace Kolvir.VirtualDom;

/// <summary>What a <see cref="ComponentMount"/> needs from the component it renders.</summary>
/// <remarks>
/// <para>
/// The mount's half of the protocol, and only that half: describe a tree, and be told which mount owns
/// it and when that stops being true. A component base class of one's own implements these three
/// members and gets the mount, the deferred render, the patching and composition with it, because
/// nothing above this interface asks the component anything else.
/// </para>
/// <para>
/// <see cref="Component.SetState"/> is deliberately not here. It is the <em>application-facing</em>
/// half - the call an event handler makes - and the mount never calls it, so requiring it would force
/// every implementation to reproduce a spelling that the mount has no opinion about. What the mount
/// does need is the primitive underneath it, <see cref="ComponentMount.Invalidate"/>, which is public
/// for exactly this reason: that is what a base class calls from its own state setter.
/// </para>
/// <para>
/// <see cref="Component"/> is the supplied implementation and the one to derive from unless there is a
/// reason not to. Implementing this interface directly is for a base class that schedules differently
/// or cannot inherit - a record, a source-generated view model, a wrapper over an existing one.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// internal sealed class Counter : IComponent
/// {
///     private ComponentMount? _mount;
///     private int _count;
///
///     public VNode Render() => new VLabel { Text = $"{_count} clicks" };
///
///     public void Attach(ComponentMount mount) => _mount = mount;
///
///     public void Detach(ComponentMount mount)
///     {
///         if (ReferenceEquals(_mount, mount))
///         {
///             _mount = null;
///         }
///     }
///
///     internal void Count()
///     {
///         _count++;
///         _mount?.Invalidate();   // the same one line SetState is
///     }
/// }
/// </code>
/// </example>
public interface IComponent
{
    /// <summary>Describes the controls this component's current state calls for.</summary>
    /// <returns>The tree to reconcile into what is mounted.</returns>
    /// <remarks>
    /// Called by the mount, once per frame in which the state changed - not once per property. It is a
    /// description and not a mutation: build and return a tree, and let the reconciler work out what
    /// changed. It must not change state either: calling <see cref="ComponentMount.Invalidate"/> from
    /// inside a render is refused, because a render that changes the state describes a tree that is
    /// already out of date.
    /// <para>
    /// A <see cref="VFragment"/> is a legal return: it puts one control per child in the parent, at the
    /// place the component holds, and an empty one describes a component with nothing to show - which
    /// puts no controls there at all.
    /// </para>
    /// </remarks>
    VNode Render();

    /// <summary>Records the mount that owns this component's tree.</summary>
    /// <param name="mount">The mount that is about to render it.</param>
    /// <remarks>
    /// Called once, before the first render, so that a state change during construction has a mount to
    /// reach. An implementation should refuse a second live mount: one component is one subtree, and a
    /// second mount would leave the first one rendering a component that no longer points at it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The component is already mounted.</exception>
    void Attach(ComponentMount mount);

    /// <summary>Forgets <paramref name="mount"/> if it is the one this component renders through.</summary>
    /// <param name="mount">The mount being disposed, or the one whose first render threw.</param>
    /// <remarks>
    /// Called when the mount goes away, and the component is usually thrown away with it - but not
    /// always: a component can be mounted again elsewhere, and one whose first render threw is still
    /// perfectly usable. So this is a reset rather than a tombstone.
    /// </remarks>
    void Detach(ComponentMount mount);
}

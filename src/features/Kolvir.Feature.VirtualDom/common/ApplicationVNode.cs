namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// A description of one application-level thing, which the application reconciler turns into the real
/// thing and then patches.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not a <see cref="VNode"/>. A control node's contract - <see cref="VNode.NodeType"/>,
/// <see cref="VNode.ApplyTo"/> and <see cref="VNode.PatchFrom"/> - is about making and configuring a
/// <see cref="Control"/>, and every one of those members would be a throwing stub here. <see cref="VNode.Children"/>
/// is a list of control children too, which is not what a window has. Keeping the two hierarchies apart
/// means a <see cref="VWindow"/> in a control tree and a <see cref="VNode"/> in an application render are
/// compile errors rather than run-time refusals, and it makes it impossible to pass an application node
/// to <see cref="VirtualDomRuntime.Mount(Control, VNode)"/> by mistake.
/// </para>
/// <para>
/// A developer does not derive from this class today: the only node an application render may return is
/// <see cref="VWindow"/>. The abstract base exists because the application reconciler works against it,
/// and because a later node - a tray icon or a menu-bar item - belongs beside a window rather than
/// inside one.
/// </para>
/// </remarks>
public abstract class ApplicationVNode
{
    /// <summary>
    /// Identifies this node among the windows a render returns, so that a reorder moves it instead of
    /// replacing it.
    /// </summary>
    /// <remarks>
    /// Reconciliation matches keyed windows by key before it matches anything by position, which is
    /// what lets a list of windows be reordered, inserted into or reversed without disturbing the OS
    /// windows that survived: a moved window keeps its focus, geometry and display state, because it
    /// was moved rather than rebuilt. Leave it null for a window whose position is its identity.
    /// </remarks>
    public object? Key { get; init; }
}

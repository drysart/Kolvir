using System.ComponentModel;

namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// A node that describes a control of a given type, which is what lets a property slot accept a node
/// for that control or for any control derived from it.
/// </summary>
/// <typeparam name="TControl">The control type the node makes.</typeparam>
/// <remarks>
/// <para>
/// A slot property is typed <c>IVNodeFor&lt;Panel&gt;?</c>, so <c>Panel1 = new VPanel { .. }</c> is an
/// identity conversion and <c>Panel1 = new VTableLayoutPanel { .. }</c> is a variance conversion:
/// <typeparamref name="TControl"/> appears in no member position, so a node that declares
/// <c>IVNodeFor&lt;TableLayoutPanel&gt;</c> is an <c>IVNodeFor&lt;Panel&gt;</c> as well - and an
/// <c>IVNodeFor&lt;Control&gt;</c>, and every type in between. A node whose control is not a
/// <c>Panel</c> does not convert at all, which is what makes the mistake a compile error at the
/// assignment rather than a control that quietly appears somewhere unexpected.
/// </para>
/// <para>
/// One instantiation per node class - its own control type - and not one per control in its chain:
/// variance supplies the rest. <see cref="VNode"/> is not generic, so the node the reconciler mounts
/// is reached through <see cref="Node"/>; nothing downstream of the property asks which instantiation
/// it came from.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// new VSplitContainer
/// {
///     Panel1 = new VPanel { Padding = new Thickness(8f), Children = [new VLabel { Text = "left" }] },
///     Panel2 = new VTableLayoutPanel { RowStyles = [new RowStyle(SizeType.Absolute, 24f)] },
/// }
/// </code>
/// </example>
public interface IVNodeFor<out TControl> where TControl : Control
{
    /// <summary>The node that describes the control.</summary>
    /// <remarks>
    /// Implemented explicitly as <c>=&gt; this</c> on every generated node class, and read by the
    /// reconciler and by nothing else. It is a member rather than the interface deriving from a
    /// non-generic <c>IVNode</c> because a base interface would have to be named by every node class
    /// anyway while giving the reconciler a second type to test for.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    VNode Node { get; }
}

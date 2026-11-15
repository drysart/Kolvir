using System.ComponentModel;

namespace Kolvir.VirtualDom;

/// <summary>
/// Marks a node whose control lays no children out, so that assigning <see cref="VNode.Children"/> to
/// it can be reported at build time.
/// </summary>
/// <remarks>
/// <para>
/// Every <see cref="Control"/> has a <see cref="Control.Controls"/> collection, so a leaf control
/// accepts children without complaint and then paints none of them - the tree is silently wrong
/// rather than broken. The reconciler cannot tell either: it recurses into whatever control a node
/// produced, which is the only behaviour that lets a container be written once.
/// </para>
/// <para>
/// So the generator records the answer instead. It applies this attribute to a generated node when
/// the control type does not override <c>OnLayout</c>, which is where a container decides where its
/// children go: a type that never overrides it never shows them. The analyzer reads the attribute and
/// raises VD011 on an assignment to <see cref="VNode.Children"/>.
/// </para>
/// <para>
/// Generated code applies it across assemblies, which is why it is public rather than internal; it is
/// not meant to be applied by hand. A hand-written node can carry it too, and then gets the same
/// check.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class LeafNodeAttribute : Attribute;

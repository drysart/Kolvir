namespace Kolvir;

/// <summary>
/// Marks a non-control type that a virtual-DOM generator may describe with a node class of its own.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="Control"/> gets a node class because it can be created and configured. Some controls
/// instead hold a collection of lightweight element objects - <see cref="RichLabel.Spans"/> is the
/// case this exists for - and those elements are not controls: they have no window, no parent chain
/// and no <c>Controls</c> collection. The element type is still something a render can describe, so
/// it gets the same treatment: this attribute declares it, and the generator emits a node class for
/// it that a generated collection property can be typed with.
/// </para>
/// <para>
/// The attribute lives in <c>Kolvir</c> rather than in the virtual-DOM feature because the element
/// type it marks does too: <see cref="TextSpan"/> is declared by the core assembly, and a core type
/// cannot name a type from a package that references core. It carries no behaviour and no state, so
/// an application that never uses the virtual DOM never reaches anything for it.
/// </para>
/// <para>
/// An element type has to be a concrete, accessible, non-generic class with an accessible
/// parameterless constructor, because its node class creates instances of it directly. An element
/// node describes the element's own members; a control-typed member is a slot, exactly as it is on
/// a control node.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class VirtualDomElementAttribute : Attribute;

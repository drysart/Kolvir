namespace Kolvir;

/// <summary>
/// Marks a non-control type that a generator may describe with a generated description class of its
/// own, so a control's collection of those elements can be managed declaratively.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="Control"/> gets a generated description because it can be created and configured.
/// Some controls instead hold a collection of lightweight element objects - <see cref="RichLabel.Spans"/>
/// is the case this exists for - and those elements are not controls: they have no window, no parent
/// chain and no <c>Controls</c> collection. The element type is still something a render can describe,
/// so it gets the same treatment: this attribute declares it, and the generator emits a description
/// class for it that a generated collection property can be typed with.
/// </para>
/// <para>
/// The attribute lives in <c>Kolvir</c> rather than in the feature that consumes it because the element
/// type it marks does too: <see cref="TextSpan"/> is declared by the core assembly, and a core type
/// cannot name a type from a package that references core. It carries no behaviour and no state, so an
/// application that never uses a generated description never reaches anything for it. Its name names no
/// particular consumer, because core's public vocabulary outlives any one of them.
/// </para>
/// <para>
/// An element type has to be a concrete, accessible, non-generic class with an accessible
/// parameterless constructor, because its description class creates instances of it directly. An
/// element description covers the element's own members; a control-typed member is a slot, exactly as
/// it is on a control node.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class VirtualizableElementAttribute : Attribute;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Kolvir.Feature.VirtualDom.Generators;

/// <summary>One property the node class will expose.</summary>
/// <param name="Name">The property name, shared by the control and the node.</param>
/// <param name="Type">The property's type, fully qualified and with its nullable annotation.</param>
/// <param name="Field">The backing field's name.</param>
/// <param name="Pristine">The static field holding the value the control's constructor leaves, or the declared constant.</param>
/// <param name="PristineLiteral">
/// What that field is initialized with, when a <c>[VNodePropertyUnsetValue]</c> declared it as a
/// constant; null when the value has to be read from a constructed control.
/// </param>
/// <param name="AddHandler">The handler-adding method, when this property is an event.</param>
/// <param name="RemoveHandler">The handler-removing method, when this property is an event.</param>
/// <param name="Container">
/// The static class the member belongs to when it is an extension member, fully qualified; null when
/// it is a member of the control itself.
/// </param>
/// <param name="Accessor">
/// The name the compiler lowered the member to on that class: the declared member name, which an
/// override renames for the node without renaming it here.
/// </param>
/// <param name="Receiver">
/// The receiver type an extension member's accessor takes, fully qualified; null for a native member.
/// An actuator is shared by every node class the member applies to, so it casts the control to this
/// rather than to any one node's control type.
/// </param>
/// <param name="Documentation">
/// The member's documentation, reduced to what is valid on a property and ready to be written between
/// <c>///</c> markers, or null when there is none to be had.
/// </param>
/// <param name="DocumentationId">
/// The member's documentation ID, which is how the reference documentation is indexed; null when the
/// symbol has no documentation ID at all.
/// </param>
/// <param name="DocumentationAssembly">
/// The assembly the member is declared in, whose documentation file the ID is looked up in.
/// </param>
internal sealed record PropertyModel(
    string Name,
    string Type,
    string Field,
    string? Pristine,
    string? PristineLiteral,
    string? AddHandler,
    string? RemoveHandler,
    string? Container,
    string Accessor,
    string? Receiver = null,
    string? Documentation = null,
    string? DocumentationId = null,
    string? DocumentationAssembly = null)
{
    internal bool IsEvent => AddHandler is not null;

    /// <summary>
    /// Whether this is a handler pair the node gets from an extension class, which is the half moved
    /// behind first-use actuators: it has no generated backing field and no per-member apply line.
    /// </summary>
    internal bool IsExtensionEvent => IsEvent && Container is not null;

    /// <summary>Whether the value has to be read from a constructed control rather than being a declared constant.</summary>
    internal bool NeedsPristine => Pristine is not null && PristineLiteral is null;

    /// <summary>Writes the assignment of this member on <paramref name="target"/>, native or extension alike.</summary>
    internal string Assign(string target, string value) =>
        Container is null
            ? target + "." + Accessor + " = " + value
            : Container + ".set_" + Accessor + "(" + target + ", " + value + ")";

    /// <summary>Writes the read of this member on <paramref name="target"/>, native or extension alike.</summary>
    internal string Read(string target) =>
        Container is null
            ? target + "." + Accessor
            : Container + ".get_" + Accessor + "(" + target + ")";

    /// <summary>Writes the call that attaches the handler, native or extension alike.</summary>
    internal string Attach(string target, string handler) =>
        Container is null
            ? target + "." + AddHandler + "(" + handler + ")"
            : Container + "." + AddHandler + "(" + target + ", " + handler + ")";

    /// <summary>Writes the call that detaches the handler, native or extension alike.</summary>
    internal string Detach(string target, string handler) =>
        Container is null
            ? target + "." + RemoveHandler + "(" + handler + ")"
            : Container + "." + RemoveHandler + "(" + target + ", " + handler + ")";
}

/// <summary>
/// A name more than one declaration wanted, which the node exposes as an inert placeholder instead.
/// </summary>
/// <param name="Name">The member name in dispute.</param>
/// <param name="Contenders">One claim per contender: <c>containing type::member</c>.</param>
internal sealed record MemberPlaceholderModel(string Name, ImmutableArray<string> Contenders);

/// <summary>A registered control, and the node class it becomes.</summary>
/// <param name="NodeName">The generated class's name.</param>
/// <param name="Accessibility">The accessibility the generated class gets, matching the control's.</param>
/// <param name="ControlType">The control's type, fully qualified.</param>
/// <param name="ControlMetadataName">The same type the way a reader names it, for diagnostics.</param>
/// <param name="AcceptsChildren">Whether the control arranges children, so that the node is not a leaf.</param>
/// <param name="Properties">The properties the node exposes.</param>
/// <param name="Placeholders">The names this control's node cannot expose, and why.</param>
/// <param name="Chain">The control and its base classes, so an extension member's receiver can be compared against them.</param>
/// <param name="Interfaces">The interfaces the control implements, for the same reason.</param>
/// <param name="Location">Where the registration is, to report diagnostics against.</param>
internal sealed record ControlModel(
    string NodeName,
    string Accessibility,
    string ControlType,
    string ControlMetadataName,
    bool AcceptsChildren,
    ImmutableArray<PropertyModel> Properties,
    ImmutableArray<MemberPlaceholderModel> Placeholders,
    ImmutableArray<string> Chain,
    ImmutableArray<string> Interfaces,
    Location Location);

/// <summary>
/// A node class name two controls wanted, which is emitted as one inert placeholder class instead.
/// </summary>
/// <param name="Name">The class name in dispute.</param>
/// <param name="Accessibility">
/// The accessibility the class gets: public when any contender is public, internal when every one of
/// them is internal.
/// </param>
/// <param name="Contenders">One claim per contender: the control type's identity.</param>
internal sealed record TypePlaceholderModel(string Name, string Accessibility, ImmutableArray<string> Contenders);

/// <summary>One manifest class, and the controls registered on it.</summary>
/// <param name="Name">The manifest's own name, which makes its generated file name unique.</param>
/// <param name="Namespace">The namespace the node classes are emitted into, or null for the global one.</param>
/// <param name="Controls">The controls registered on the manifest.</param>
/// <param name="Placeholders">Node class names emitted as inert placeholders in this manifest's file.</param>
/// <param name="Overrides">The assembly's <c>[VNodeExtensionPropertyName]</c> declarations, read once per manifest.</param>
/// <param name="Diagnostics">What reading the manifest found wrong.</param>
/// <param name="Location">Where the manifest is, which is where a problem with a swept control is reported.</param>
/// <param name="Sweeps">
/// Whether the manifest declares <c>[VirtualDomAll]</c> rather than naming controls, in which case
/// <paramref name="Controls"/> stays empty and every available control is added when the output is
/// produced.
/// </param>
internal sealed record ManifestModel(
    string Name,
    string? Namespace,
    ImmutableArray<ControlModel> Controls,
    ImmutableArray<TypePlaceholderModel> Placeholders,
    ImmutableArray<OverrideModel> Overrides,
    ImmutableArray<DiagnosticInfo> Diagnostics,
    Location Location,
    bool Sweeps = false);

/// <summary>A diagnostic found while reading a manifest, reported when the output is produced.</summary>
/// <param name="Descriptor">Which diagnostic.</param>
/// <param name="Location">Where to point at.</param>
/// <param name="Arguments">The message arguments.</param>
internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, Location Location, ImmutableArray<string> Arguments)
{
    internal Diagnostic ToDiagnostic() => Diagnostic.Create(Descriptor, Location, Arguments.ToArray<object>());
}

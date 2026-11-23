using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Kolvir.Feature.VirtualDom.Generators;

/// <summary>One property the node class will expose.</summary>
/// <param name="Name">The property name, shared by the control and the node.</param>
/// <param name="Type">The property's type, fully qualified and with its nullable annotation.</param>
/// <param name="Field">The backing field's name.</param>
/// <param name="Pristine">The static field holding the value the control's constructor leaves.</param>
/// <param name="AddHandler">The handler-adding method, when this property is an event.</param>
/// <param name="RemoveHandler">The handler-removing method, when this property is an event.</param>
internal sealed record PropertyModel(
    string Name,
    string Type,
    string Field,
    string? Pristine,
    string? AddHandler,
    string? RemoveHandler)
{
    internal bool IsEvent => AddHandler is not null;
}

/// <summary>One registered control, and the node class it becomes.</summary>
/// <param name="NodeName">The generated class's name.</param>
/// <param name="Accessibility">The accessibility the generated class gets, matching the control's.</param>
/// <param name="ControlType">The control's type, fully qualified.</param>
/// <param name="AcceptsChildren">Whether the control arranges children, so that the node is not a leaf.</param>
/// <param name="Properties">The properties the node exposes.</param>
/// <param name="Location">Where the registration is, to report diagnostics against.</param>
internal sealed record ControlModel(
    string NodeName,
    string Accessibility,
    string ControlType,
    bool AcceptsChildren,
    ImmutableArray<PropertyModel> Properties,
    Location Location);

/// <summary>One manifest class, and the controls registered on it.</summary>
/// <param name="Name">The manifest's own name, which makes its generated file name unique.</param>
/// <param name="Namespace">The namespace the node classes are emitted into, or null for the global one.</param>
/// <param name="Controls">The controls registered on the manifest.</param>
/// <param name="Diagnostics">What reading the manifest found wrong.</param>
internal sealed record ManifestModel(
    string Name,
    string? Namespace,
    ImmutableArray<ControlModel> Controls,
    ImmutableArray<DiagnosticInfo> Diagnostics);

/// <summary>A diagnostic found while reading a manifest, reported when the output is produced.</summary>
/// <param name="Descriptor">Which diagnostic.</param>
/// <param name="Location">Where to point at.</param>
/// <param name="Arguments">The message arguments.</param>
internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, Location Location, ImmutableArray<string> Arguments)
{
    internal Diagnostic ToDiagnostic() => Diagnostic.Create(Descriptor, Location, Arguments.ToArray<object>());
}

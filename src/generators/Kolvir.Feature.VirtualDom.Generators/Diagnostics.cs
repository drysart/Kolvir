using Microsoft.CodeAnalysis;

namespace Kolvir.Feature.VirtualDom.Generators;

/// <summary>The diagnostics the generator and the analyzer report.</summary>
internal static class Diagnostics
{
    private const string Category = "VirtualDom";

    internal static readonly DiagnosticDescriptor NotAControl = new(
        "VD001",
        "Registered type is not a control",
        "'{0}' is not a Control, so it cannot be made into a node",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor NotConstructible = new(
        "VD002",
        "Registered control cannot be created",
        "'{0}' has no accessible parameterless constructor, so a node cannot create it",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateNodeName = new(
        "VD003",
        "Two registrations produce the same node name",
        "'{0}' is used twice; give one of the registrations a different Name",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateRegistration = new(
        "VD004",
        "Control registered twice",
        "'{0}' is registered more than once",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor NameIsNotAnIdentifier = new(
        "VD005",
        "Registration name is not a valid identifier",
        "'{0}' is not a valid C# identifier, so it cannot name a node class",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ManifestIsNotPartial = new(
        "VD007",
        "Registration class must be partial",
        "'{0}' must be declared partial",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MemberNameCollides = new(
        "VD012",
        "Property cannot be flattened onto the node",
        "'{0}' has the same name as {1} on the generated node class, so the reconciler cannot set it; rename the member on the control or leave it unregistered",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor NullForNonNullable = new(
        "VD010",
        "A null cannot be assigned to this node property",
        "'{0}' is not nullable on the control, so a node cannot set it to null; assign a value, or leave the property out to let the control keep or restore its own",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ChildrenOnLeaf = new(
        "VD011",
        "This node's control shows no children",
        "'{0}' produces a control that does not arrange children, so the nodes assigned to it would never be shown",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}

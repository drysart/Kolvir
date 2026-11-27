using Microsoft.CodeAnalysis;

namespace Kolvir.Feature.VirtualDom.Generators;

/// <summary>The diagnostics the generator and the analyzers report.</summary>
internal static class Diagnostics
{
    private const string Category = "VirtualDom";

    /// <summary>Where the rules are explained, anchored per subject so a diagnostic's link lands on it.</summary>
    private const string HelpBase = "https://github.com/drysart/Kolvir/blob/develop/dev-docs/EXTENSION-MEMBERS-ON-VNODES.md";

    internal const string ConflictHelp = HelpBase + "#6-conflicts-and-the-analyzer-tripwire";

    internal const string NodeNameHelp = HelpBase + "#7-duplicate-node-class-names";

    internal const string OverrideHelp = HelpBase + "#5-naming-and-the-overrides";

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

    internal static readonly DiagnosticDescriptor DuplicateRegistration = new(
        "VD004",
        "Control registered twice",
        "'{0}' is registered more than once",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor NameIsNotAnIdentifier = new(
        "VD005",
        "Name is not a valid identifier",
        "'{0}' is not a valid C# identifier, so it cannot name a generated member",
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

    internal static readonly DiagnosticDescriptor MemberNameCollides = new(
        "VD012",
        "Property cannot be flattened onto the node",
        "'{0}' has the same name as {1} on the generated node class, so the reconciler cannot set it; rename the member on the control or leave it unregistered",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>Reported where a node property whose name two extension members claimed is used.</summary>
    internal static readonly DiagnosticDescriptor MemberNameConflict = new(
        "VD013",
        "Two extension members want the same node property",
        "'{0}' is claimed by {1}, so the generated node cannot expose it; resolving either claim resolves the conflict: {2}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: ConflictHelp);

    /// <summary>Reported where a node class name that two controls claimed is used.</summary>
    internal static readonly DiagnosticDescriptor NodeNameConflict = new(
        "VD014",
        "Two controls want the same node class name",
        "'{0}' is claimed by {1}, so no node class was generated for either control; resolving either claim resolves the conflict: {2}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: NodeNameHelp);

    /// <summary>Reported on an override that renames an extension member onto a name the node already has.</summary>
    internal static readonly DiagnosticDescriptor ExtensionNameIsTaken = new(
        "VD015",
        "An overridden extension member name is already taken",
        "The extension member '{0}' is exposed as '{1}', which '{2}' already gives the node; choose a name the node does not already have",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: OverrideHelp);

    /// <summary>Reported on a <c>[VirtualizableElement]</c> type a description class cannot be made for.</summary>
    internal static readonly DiagnosticDescriptor ElementIsNotUsable = new(
        "VD016",
        "Marked element type cannot be described",
        "'{0}' is marked [VirtualizableElement] but cannot be made into a description class: an element type must be a concrete, accessible, non-generic class with an accessible parameterless constructor, and must not be a Control",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>Reported when an element description class's name is already a control node's in a namespace.</summary>
    internal static readonly DiagnosticDescriptor ElementNameConflict = new(
        "VD017",
        "Element description would reuse a node class name",
        "'{0}' would be the description class for '{1}', but '{2}' already uses that name in the same namespace; no description class was generated, so the collection property it would type is left off the node",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}


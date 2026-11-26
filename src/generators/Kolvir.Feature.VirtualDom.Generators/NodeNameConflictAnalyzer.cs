using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Kolvir.Feature.VirtualDom.Generators;

/// <summary>
/// Reports a generated name that more than one declaration claimed, where that name is <em>used</em>.
/// </summary>
/// <remarks>
/// <para>
/// The generator cannot resolve such a name without choosing one of the claimants silently, so it emits
/// an inert placeholder instead: a property that holds nothing (VD013) or a class that creates nothing
/// (VD014), carrying <c>[VNodeNameConflict]</c> and no other member. The conflict is a fact about the
/// application rather than about the generated code, and it is worth reporting only when something
/// depends on the name - so the analyzer registers for references and says nothing about a placeholder
/// nobody names.
/// </para>
/// <para>
/// Operations rather than syntax: a property reference covers the object initializer a tree is
/// written with, an assignment written out as a statement, and the target of a compound assignment
/// (<c>+=</c> wraps one), and an object creation covers <c>new VButtonNode()</c> however it is spelled.
/// The attribute is matched by metadata name, so this analyzer needs no reference to the assembly that
/// declares it.
/// </para>
/// <para>
/// The message is written here rather than baked into the attribute, and it carries the fix: the
/// attribute lines to paste, one per claimant, with the name left for the reader to choose.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NodeNameConflictAnalyzer : DiagnosticAnalyzer
{
    private const string MarkerName = "Kolvir.Feature.VirtualDom.VNodeNameConflictAttribute";
    private const string OverrideAttribute = "VNodeExtensionPropertyName";
    private const string RegistrationAttribute = "VirtualDom";
    private const string MemberSeparator = "::";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [Diagnostics.MemberNameConflict, Diagnostics.NodeNameConflict];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzePropertyReference, OperationKind.PropertyReference);
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;

        Report(context, reference.Property, isMember: true);
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;

        if (creation.Type is { } type)
        {
            Report(context, type, isMember: false);
        }
    }

    private static void Report(OperationAnalysisContext context, ISymbol symbol, bool isMember)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != MarkerName
                || attribute.ConstructorArguments.Length != 2
                || attribute.ConstructorArguments[0].Value is not string name)
            {
                continue;
            }

            var claims = Contenders(attribute.ConstructorArguments[1]);

            context.ReportDiagnostic(Diagnostic.Create(
                isMember ? Diagnostics.MemberNameConflict : Diagnostics.NodeNameConflict,
                context.Operation.Syntax.GetLocation(),
                name,
                string.Join(", ", claims.Select(claim => "'" + claim + "'")),
                Suggestions(claims, isMember)));
        }
    }

    private static string[] Contenders(TypedConstant constant) =>
        constant.Kind == TypedConstantKind.Array
            ? [.. constant.Values.Select(value => value.Value as string).Where(value => value is { Length: > 0 })!]
            : [];

    /// <summary>The attribute lines that would resolve the conflict, one per claimant.</summary>
    private static string Suggestions(IEnumerable<string> claims, bool isMember) =>
        string.Join(
            "\n",
            claims.Select(claim => isMember ? OverrideFor(claim) : RegistrationFor(claim)));

    private static string OverrideFor(string claim)
    {
        var separator = claim.IndexOf(MemberSeparator, StringComparison.Ordinal);

        // A member claim is "containing type::member"; anything else cannot be worded as an override.
        if (separator < 0)
        {
            return string.Empty;
        }

        var container = claim.Substring(0, separator);
        var member = claim.Substring(separator + MemberSeparator.Length);

        return "[assembly: " + OverrideAttribute + "(typeof(" + container + "), \"" + member + "\", \"<new name>\")]";
    }

    private static string RegistrationFor(string claim) =>
        "[assembly: " + RegistrationAttribute + "(typeof(" + claim + "), Name = \"<new name>\")]";
}

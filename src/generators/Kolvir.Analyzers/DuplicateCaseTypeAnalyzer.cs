using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kolvir.Analyzers;

/// <summary>
/// A union whose case types repeat is broken by definition: the case would have two constructors
/// and two conversion operators with the same signature. The compiler reports that collision
/// somewhere else, often as an error on the instantiation, so this rule names the actual mistake at
/// every place the type is written.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateCaseTypeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [Diagnostics.DuplicateCaseType];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.GenericName);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var name = (GenericNameSyntax)context.Node;

        // A cheap prefilter only. The type is identified by its symbol below, never by this name.
        if (name.Identifier.ValueText != "Union")
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol is not INamedTypeSymbol type
            || !UnionSymbols.IsKolvirUnion(type)
            || type.IsUnboundGenericType
            || type.TypeArguments.Any(UnionSymbols.ContainsErrorType))
        {
            return;
        }

        var duplicates = RepeatedCases(type);
        if (duplicates.Count == 0)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.DuplicateCaseType,
            name.GetLocation(),
            UnionSymbols.Format(type),
            string.Join(", ", duplicates)));
    }

    private static List<string> RepeatedCases(INamedTypeSymbol type)
    {
        var cases = type.TypeArguments;
        var duplicates = new List<string>();

        for (var i = 0; i < cases.Length; i++)
        {
            for (var j = i + 1; j < cases.Length; j++)
            {
                if (!UnionSymbols.AreSameRuntimeType(cases[i], cases[j]))
                {
                    continue;
                }

                var name = UnionSymbols.Format(cases[i]);
                if (!duplicates.Contains(name))
                {
                    duplicates.Add(name);
                }
            }
        }

        return duplicates;
    }
}

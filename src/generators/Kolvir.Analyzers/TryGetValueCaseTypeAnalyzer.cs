using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kolvir.Analyzers;

/// <summary>
/// <c>Union&lt;...&gt;.TryGetValue&lt;T&gt;</c> answers only for the case types the union was
/// declared with. A type argument outside that set compiles and throws
/// <see cref="ArgumentException"/> when the line runs, which may be in a branch that rarely runs.
/// </summary>
/// <remarks>
/// The rule is deliberately quiet whenever the answer could depend on a run-time instantiation:
/// unresolved types, dynamic, type parameters, native integers and tuples all skip. A false
/// positive here is a build error in an application, which is worse than a missed report.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TryGetValueCaseTypeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [Diagnostics.TryGetValueCaseType];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (!UnionSymbols.TryGetUnionTryGetValue(
                invocation,
                context.SemanticModel,
                context.CancellationToken,
                out var method,
                out var union))
        {
            return;
        }

        var typeArgument = method.TypeArguments[0];

        if (UnionSymbols.IsRuntimeSensitive(typeArgument)
            || union.TypeArguments.Any(UnionSymbols.IsRuntimeSensitive)
            || UnionSymbols.IsCaseType(union, typeArgument))
        {
            return;
        }

        var inherited = UnionSymbols.HasInheritedCase(union)
            ? "; the InheritedValue case is the one PropertyValue.Inherited assigns"
            : string.Empty;

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.TryGetValueCaseType,
            UnionSymbols.TypeArgumentLocation(invocation),
            UnionSymbols.Format(typeArgument),
            UnionSymbols.Format(union),
            UnionSymbols.CaseList(union),
            inherited));
    }
}

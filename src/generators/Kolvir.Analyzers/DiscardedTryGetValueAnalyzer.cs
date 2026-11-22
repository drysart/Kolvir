using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kolvir.Analyzers;

/// <summary>
/// A <c>TryGetValue</c> call used as a whole statement throws its answer away, and the <c>out</c>
/// value then stays <c>default</c> whenever the stored case is a different one. That is usually a
/// bug and occasionally deliberate, so this is a warning rather than an error, and an explicit
/// <c>_ = ...</c> suppresses it.
/// </summary>
/// <remarks>
/// Only a top-level expression statement is reported. An assignment, a condition, a return, a
/// negation, an argument or a lambda body is a use of the result and is left alone. A non-case
/// type argument is KOLVIR0001's business and is skipped here.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DiscardedTryGetValueAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [Diagnostics.DiscardedTryGetValue];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ExpressionStatement);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var statement = (ExpressionStatementSyntax)context.Node;

        if (statement.Expression is not InvocationExpressionSyntax invocation
            || !UnionSymbols.TryGetUnionTryGetValue(
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
            || !UnionSymbols.IsCaseType(union, typeArgument))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.DiscardedTryGetValue,
            invocation.GetLocation(),
            UnionSymbols.Format(typeArgument)));
    }
}

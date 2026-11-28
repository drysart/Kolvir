using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kolvir.Analyzers;

/// <summary>
/// A union value that was explicitly default-initialised has no case. <c>TryGetValue</c> answers
/// false for every case and <c>Switch</c>/<c>Match</c> refuse it, so the value cannot be used for
/// anything; the application meant to assign a case.
/// </summary>
/// <remarks>
/// Only explicit forms are diagnosed: a target-typed <c>default</c>, <c>default(Union&lt;...&gt;)</c>
/// and parameterless <c>new Union&lt;...&gt;()</c>. A field or property that is merely never
/// assigned needs dataflow and is deliberately out of scope, because a struct defaults silently
/// everywhere. The one exception inside the union's own constructors - <c>this = default;</c> to
/// zero the fields before assigning the case - is not a value that escapes and is skipped. So is a
/// parameter's default value: <c>Union&lt;..., InheritedValue&gt; value = default</c> is the
/// omitted-argument sentinel, and the callee checks <c>IsUnspecified</c> before reading a
/// case, so diagnosing it would make the union unusable as an optional parameter.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DefaultUnionAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [Diagnostics.DefaultUnion];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            Analyze,
            SyntaxKind.DefaultExpression,
            SyntaxKind.DefaultLiteralExpression,
            SyntaxKind.ObjectCreationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is ObjectCreationExpressionSyntax creation
            && (creation.ArgumentList?.Arguments.Count is > 0 || creation.Initializer is not null))
        {
            return;
        }

        if (IsZeroingThis(context.Node) || IsParameterDefault(context.Node))
        {
            return;
        }

        var typeInfo = context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken);

        if ((typeInfo.Type ?? typeInfo.ConvertedType) is not INamedTypeSymbol type
            || !UnionSymbols.IsKolvirUnion(type)
            || type.TypeArguments.Any(UnionSymbols.ContainsErrorType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.DefaultUnion,
            context.Node.GetLocation(),
            UnionSymbols.Format(type)));
    }

    /// <summary>
    /// Whether the expression is a parameter's default value, which is the optional-argument
    /// spelling of a union rather than a value the method reads without checking it.
    /// </summary>
    private static bool IsParameterDefault(SyntaxNode node)
    {
        var current = node;

        while (current.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            current = parenthesized;
        }

        return current.Parent is EqualsValueClauseSyntax { Parent: ParameterSyntax };
    }

    /// <summary>
    /// Whether the default/new expression initialises <c>this</c> in a constructor, which is how a
    /// struct zeroes its fields before assigning the real case. That value never escapes.
    /// </summary>
    private static bool IsZeroingThis(SyntaxNode node)
    {
        var current = node;

        while (current.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            current = parenthesized;
        }

        return current.Parent is AssignmentExpressionSyntax assignment
            && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            && assignment.Right == current
            && assignment.Left is ThisExpressionSyntax
            && assignment.FirstAncestorOrSelf<ConstructorDeclarationSyntax>() is not null;
    }
}

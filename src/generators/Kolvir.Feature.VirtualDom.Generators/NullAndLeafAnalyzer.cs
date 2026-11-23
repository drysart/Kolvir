using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kolvir.Feature.VirtualDom.Generators;

/// <summary>
/// The half of the feature's build-time checking that the compiler cannot do: a null assigned to a
/// property that cannot hold one, and children assigned to a node that will never show them.
/// </summary>
/// <remarks>
/// <para>
/// Both are reported as errors rather than warnings, which is the point of having an analyzer at all.
/// The compiler already sees the null case - a generated property has the control property's own
/// type, so the assignment is CS8625 or CS8604 - but as a warning, and not at all in a
/// <c>#nullable disable</c> file, where the null would be stored silently and then thrown from the
/// control's setter as an <see cref="ArgumentNullException"/>. A package cannot promote those
/// warnings for an application without promoting them across everything else that application
/// writes, so it reports its own diagnostic instead.
/// </para>
/// <para>
/// The analyzer reads ordinary C# - an assignment to a property of a <c>VNode</c>, wherever it
/// appears - so it covers the object initializer a tree is written with and the same assignment
/// written out as a statement, and it needs to know nothing about the generated classes beyond the
/// attribute they carry.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NullAndLeafAnalyzer : DiagnosticAnalyzer
{
    private const string VNodeName = "Kolvir.Feature.VirtualDom.VNode";
    private const string LeafNodeName = "Kolvir.Feature.VirtualDom.LeafNodeAttribute";
    private const string ChildrenName = "Children";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [Diagnostics.NullForNonNullable, Diagnostics.ChildrenOnLeaf];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol is not IPropertySymbol property
            || property.ContainingType is null
            || !IsNode(property.ContainingType))
        {
            return;
        }

        if (property.Name == ChildrenName)
        {
            var node = ReceiverType(assignment, context);

            // `Children = []` says the same thing as not assigning it at all, so it is not worth an
            // error even on a node that could never show a child.
            if (IsLeaf(node) && !IsEmptyCollection(assignment.Right))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.ChildrenOnLeaf,
                    assignment.GetLocation(),
                    node!.Name));
            }

            return;
        }

        if (property.Type.IsValueType || property.NullableAnnotation != NullableAnnotation.NotAnnotated)
        {
            return;
        }

        var right = Unwrap(assignment.Right);

        // `null!` is the caller overruling the analysis, so the run-time backstop is the right thing
        // to hear from and the build stays quiet.
        if (right.IsKind(SyntaxKind.SuppressNullableWarningExpression))
        {
            return;
        }

        if (!CouldBeNull(right, context))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.NullForNonNullable,
            right.GetLocation(),
            property.Name));
    }

    private static bool IsNode(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == VNodeName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLeaf(INamedTypeSymbol? type) =>
        type is not null
        && type.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == LeafNodeName);

    /// <summary>
    /// The node type being described: the type the member is accessed through, or the class of the
    /// object-creation expression an object initializer belongs to.
    /// </summary>
    private static INamedTypeSymbol? ReceiverType(AssignmentExpressionSyntax assignment, SyntaxNodeAnalysisContext context)
    {
        if (assignment.Left is MemberAccessExpressionSyntax access)
        {
            return context.SemanticModel.GetTypeInfo(access.Expression, context.CancellationToken).Type as INamedTypeSymbol;
        }

        var creation = assignment.FirstAncestorOrSelf<BaseObjectCreationExpressionSyntax>();

        return creation is null
            ? null
            : context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type as INamedTypeSymbol;
    }

    /// <summary>
    /// Whether the assigned expression could be null: a literal null, a possibly-null value, or a
    /// <c>default</c> of a reference type. The first of those is answered by the constant value rather
    /// than by nullability analysis, because nullability analysis says nothing at all in a file that
    /// has turned the nullable context off - which is exactly the case this diagnostic exists for.
    /// </summary>
    private static bool CouldBeNull(ExpressionSyntax right, SyntaxNodeAnalysisContext context)
    {
        var constant = context.SemanticModel.GetConstantValue(right, context.CancellationToken);

        if (constant.HasValue && constant.Value is null)
        {
            return true;
        }

        var flow = context.SemanticModel.GetTypeInfo(right, context.CancellationToken).Nullability.FlowState;

        return flow is NullableFlowState.MaybeNull;
    }

    /// <summary>Whether the value is an empty collection, which says the same thing as saying nothing.</summary>
    private static bool IsEmptyCollection(ExpressionSyntax right) =>
        Unwrap(right) switch
        {
            CollectionExpressionSyntax collection => collection.Elements.Count == 0,
            ImplicitArrayCreationExpressionSyntax array => array.Initializer.Expressions.Count == 0,
            ArrayCreationExpressionSyntax array => array.Initializer is null || array.Initializer.Expressions.Count == 0,
            ObjectCreationExpressionSyntax creation => creation.Initializer is null || creation.Initializer.Expressions.Count == 0,
            _ => false,
        };

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }
}

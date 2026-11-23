using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kolvir.Analyzers;

/// <summary>
/// Symbol recognition shared by the union diagnostics. Everything here is deliberately
/// conservative: a wrong answer costs a consumer build, so a shape that cannot be classified
/// with certainty is left alone.
/// </summary>
internal static class UnionSymbols
{
    private const string NamespaceName = "Kolvir";
    private const string TypeName = "Union";
    private const string TryGetValueName = "TryGetValue";

    /// <summary>
    /// Whether <paramref name="type"/> is a constructed <c>Kolvir.Union&lt;...&gt;</c>. The check
    /// is on the original definition and its namespace, never on the spelling of a generic name.
    /// </summary>
    internal static bool IsKolvirUnion(INamedTypeSymbol? type) =>
        type is not null
        && type.OriginalDefinition.Name == TypeName
        && type.OriginalDefinition.ContainingType is null
        && type.OriginalDefinition.ContainingNamespace.ToDisplayString() == NamespaceName
        && type.OriginalDefinition.Arity is >= 2 and <= 4;

    /// <summary>Whether the symbol, or anything nested in it, is an unresolved type.</summary>
    internal static bool ContainsErrorType(ITypeSymbol type) =>
        type.TypeKind == TypeKind.Error
        || (type is INamedTypeSymbol named && named.TypeArguments.Any(ContainsErrorType))
        || (type is IArrayTypeSymbol array && ContainsErrorType(array.ElementType))
        || (type is IPointerTypeSymbol pointer && ContainsErrorType(pointer.PointedAtType));

    /// <summary>
    /// Whether the symbol, or anything nested in it, is a type parameter. An analysis of an open
    /// generic instantiation cannot know which type a caller will supply, so these are skipped.
    /// </summary>
    internal static bool ContainsTypeParameter(ITypeSymbol type) =>
        type is ITypeParameterSymbol
        || (type is INamedTypeSymbol named && named.TypeArguments.Any(ContainsTypeParameter))
        || (type is IArrayTypeSymbol array && ContainsTypeParameter(array.ElementType))
        || (type is IPointerTypeSymbol pointer && ContainsTypeParameter(pointer.PointedAtType));

    /// <summary>
    /// Whether a type is one where symbol equality can disagree with the runtime's
    /// <c>typeof</c> equality: dynamic/object, native integers, tuples and unresolved or open
    /// types. A caller that cannot decide must not report.
    /// </summary>
    internal static bool IsRuntimeSensitive(ITypeSymbol type) =>
        type.TypeKind is TypeKind.Dynamic or TypeKind.Error
        || type.IsTupleType
        || type.IsNativeIntegerType
        || ContainsTypeParameter(type);

    /// <summary>Whether <paramref name="candidate"/> is one of the union's case types.</summary>
    internal static bool IsCaseType(INamedTypeSymbol union, ITypeSymbol candidate) =>
        union.TypeArguments.Any(caseType => SymbolEqualityComparer.Default.Equals(caseType, candidate));

    /// <summary>
    /// Whether two case types are the same runtime type. <c>dynamic</c> and <c>object</c> are
    /// separate symbols but the same <c>typeof</c>, which is what the union compares.
    /// </summary>
    internal static bool AreSameRuntimeType(ITypeSymbol left, ITypeSymbol right)
    {
        if (SymbolEqualityComparer.Default.Equals(left, right))
        {
            return true;
        }

        if (left.TypeKind == TypeKind.Dynamic)
        {
            return right.SpecialType == SpecialType.System_Object;
        }

        if (right.TypeKind == TypeKind.Dynamic)
        {
            return left.SpecialType == SpecialType.System_Object;
        }

        if (left.IsNativeIntegerType && right.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr)
        {
            return true;
        }

        return right.IsNativeIntegerType && left.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr;
    }

    /// <summary>
    /// Resolves an invocation to an instance <c>Kolvir.Union&lt;...&gt;.TryGetValue&lt;T&gt;</c>.
    /// </summary>
    internal static bool TryGetUnionTryGetValue(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IMethodSymbol method,
        out INamedTypeSymbol union)
    {
        method = null!;
        union = null!;

        if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol candidate
            || candidate.IsStatic
            || candidate.Name != TryGetValueName
            || !candidate.IsGenericMethod
            || candidate.TypeArguments.Length != 1
            || candidate.Parameters.Length != 1
            || candidate.ContainingType is not INamedTypeSymbol containing
            || !IsKolvirUnion(containing))
        {
            return false;
        }

        method = candidate;
        union = containing;
        return true;
    }

    /// <summary>Formats a type the way a person writes it in the message.</summary>
    internal static string Format(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    /// <summary>The valid case types, comma separated, for a diagnostic message.</summary>
    internal static string CaseList(INamedTypeSymbol union) =>
        string.Join(", ", union.TypeArguments.Select(Format));

    /// <summary>Whether <c>InheritedValue</c> is one of the cases, so the message can name it.</summary>
    internal static bool HasInheritedCase(INamedTypeSymbol union) =>
        union.TypeArguments.Any(type =>
            type.Name == "InheritedValue"
            && type.ContainingNamespace?.ToDisplayString() == NamespaceName);

    /// <summary>
    /// The explicit type argument of an invocation, or the invocation itself when the type was
    /// inferred and has no source location of its own.
    /// </summary>
    internal static Location TypeArgumentLocation(InvocationExpressionSyntax invocation)
    {
        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Name,
            GenericNameSyntax generic => generic,
            _ => null,
        };

        return name is GenericNameSyntax { TypeArgumentList.Arguments.Count: 1 } named
            ? named.TypeArgumentList.Arguments[0].GetLocation()
            : invocation.GetLocation();
    }
}

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Kolvir.Feature.VirtualDom.Generators;

/// <summary>
/// Every control a <c>[VirtualDomAll]</c> sweep would emit a node class for, found once per
/// compilation.
/// </summary>
/// <remarks>
/// <para>
/// A control qualifies when a node could be built from it: public, concrete, non-generic, derived from
/// <c>Kolvir.Control</c>, and constructible without arguments. Everything else is dropped without a
/// diagnostic. There is nothing an application could do about a control that is abstract or that has
/// no parameterless constructor - the node factory is <c>new T()</c> - so reporting one would be a
/// message with no remedy, and the sweep is a declaration rather than an instruction to review a list.
/// </para>
/// <para>
/// The walk is the narrowed one: the compilation's own assembly plus the assemblies that can name
/// <c>Kolvir.Control</c> (see <see cref="TypeWalk"/>). It is a unit of its own - not part of reading a
/// manifest - because reading one is per manifest and this answer is per compilation.
/// </para>
/// </remarks>
internal sealed record ControlCatalog(ImmutableArray<INamedTypeSymbol> Controls)
{
    private const string ControlTypeName = "Kolvir.Control";

    /// <summary>Nothing found, which is what a compilation with no controls gets.</summary>
    internal static ControlCatalog Empty => new(ImmutableArray<INamedTypeSymbol>.Empty);

    /// <summary>Finds every control a sweep could build a node for.</summary>
    /// <param name="compilation">The compilation whose assemblies are walked.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <returns>The controls, in name order so that one sweep always writes the same file.</returns>
    internal static ControlCatalog Sweep(Compilation compilation, CancellationToken cancellationToken)
    {
        var control = compilation.GetTypeByMetadataName(ControlTypeName);

        if (control is null)
        {
            return Empty;
        }

        var found = new List<INamedTypeSymbol>();

        foreach (var assembly in TypeWalk.Around(compilation, ControlTypeName))
        {
            foreach (var type in TypeWalk.Types(assembly.GlobalNamespace, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Eligible(type, control, compilation))
                {
                    found.Add(type);
                }
            }
        }

        // The node classes are emitted in this order, and a compiler's walk order is its own business:
        // sorted, so that a sweep writes the same file from one build to the next.
        found.Sort(static (left, right) => string.CompareOrdinal(
            left.ToDisplayString(Formats.Metadata),
            right.ToDisplayString(Formats.Metadata)));

        return new ControlCatalog([.. found]);
    }

    private static bool Eligible(INamedTypeSymbol type, INamedTypeSymbol control, Compilation compilation) =>
        type.TypeKind == TypeKind.Class
        && type.DeclaredAccessibility == Accessibility.Public
        && !type.IsAbstract
        && type.Arity == 0
        && Derives(type, control)
        && type.InstanceConstructors.Any(constructor =>
            constructor.Parameters.Length == 0 && Accessible(constructor, type, compilation));

    /// <summary>
    /// Whether the generated code could call the constructor. The node classes are emitted into the
    /// compilation's own assembly, so an internal constructor counts for a control declared there and
    /// only a public one counts for a control in a referenced assembly.
    /// </summary>
    private static bool Accessible(IMethodSymbol constructor, INamedTypeSymbol type, Compilation compilation) =>
        constructor.DeclaredAccessibility == Accessibility.Public
        || (constructor.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedOrInternal
            && SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly));

    private static bool Derives(INamedTypeSymbol type, INamedTypeSymbol control)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, control))
            {
                return true;
            }
        }

        return false;
    }
}

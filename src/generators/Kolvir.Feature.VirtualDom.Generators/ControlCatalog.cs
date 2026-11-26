using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Kolvir.Feature.VirtualDom.Generators;

/// <summary>
/// Every control a <c>[VirtualDomAll]</c> sweep would emit a node class for, found once per
/// compilation.
/// </summary>
/// <remarks>
/// <para>
/// A control qualifies when a node could be built from it: concrete, non-generic, derived from
/// <c>Kolvir.Control</c>, reachable from the generated code, and constructible without arguments.
/// Everything else is dropped without a diagnostic. There is nothing an application could do about a
/// control that is abstract or that has no parameterless constructor - the node factory is
/// <c>new T()</c> - so reporting one would be a message with no remedy, and the sweep is a
/// declaration rather than an instruction to review a list.
/// </para>
/// <para>
/// Reachable means reachable from <em>where the node is written</em>: the node class is emitted into
/// the compilation's own assembly, so a control declared there may be <c>public</c>, <c>internal</c> or
/// <c>protected internal</c> and the node is emitted with the same accessibility. A control in a
/// referenced assembly has to be <c>public</c>, because nothing else is reachable from another
/// assembly. A <c>private</c> or <c>private protected</c> nested control is out either way: reaching it
/// would need the derivation the node class does not have.
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
        && !type.IsAbstract
        && type.Arity == 0
        && Derives(type, control)
        && Accessible(type, compilation)
        && type.InstanceConstructors.Any(constructor =>
            constructor.Parameters.Length == 0 && Accessible(constructor, type, compilation));

    /// <summary>
    /// Whether the generated code could name the control. The node classes are emitted into the
    /// compilation's own assembly, so an internal or protected-internal control counts when it is
    /// declared there and only a public one counts when it comes from a referenced assembly.
    /// </summary>
    private static bool Accessible(INamedTypeSymbol type, Compilation compilation) =>
        Reachable(type.DeclaredAccessibility, type, compilation);

    /// <summary>
    /// Whether the generated code could call the constructor. The same rule as for a type: declared in
    /// the compilation's own assembly is reachable from the node class, wherever it is public.
    /// </summary>
    private static bool Accessible(IMethodSymbol constructor, INamedTypeSymbol type, Compilation compilation) =>
        Reachable(constructor.DeclaredAccessibility, type, compilation);

    private static bool Reachable(Accessibility accessibility, INamedTypeSymbol owner, Compilation compilation) =>
        accessibility == Accessibility.Public
        || (accessibility is Accessibility.Internal or Accessibility.ProtectedOrInternal
            && SymbolEqualityComparer.Default.Equals(owner.ContainingAssembly, compilation.Assembly));

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

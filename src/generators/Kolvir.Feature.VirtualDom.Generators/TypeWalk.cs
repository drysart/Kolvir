using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Kolvir.Feature.VirtualDom.Generators;

/// <summary>
/// The assemblies a compilation can draw a control or an extension member from, and the walk over
/// their types.
/// </summary>
/// <remarks>
/// Both halves of the generator need the same narrowing, which is why it lives here rather than in
/// either of them: an assembly can only declare a control derived from <c>Kolvir.Control</c>, or an
/// extension member on one, if it can name <c>Kolvir.Control</c>, so it has to reference the assembly
/// that declares it. That is complete by construction rather than by heuristic, and it is what keeps
/// a sweep off the twenty thousand types a compilation can otherwise see.
/// </remarks>
internal static class TypeWalk
{
    /// <summary>
    /// The assemblies that can declare something about <paramref name="declaringTypeName"/>: the
    /// compilation's own, the one that declares the type, and every reference that names it.
    /// </summary>
    /// <param name="compilation">The compilation being generated for.</param>
    /// <param name="declaringTypeName">The metadata name of the type that has to be nameable.</param>
    /// <returns>The assemblies, in the order they are walked.</returns>
    /// <remarks>
    /// A compilation that cannot name the type at all gets every reference rather than none, so a
    /// missing reference widens the walk instead of silently finding nothing.
    /// </remarks>
    internal static ImmutableArray<IAssemblySymbol> Around(Compilation compilation, string declaringTypeName)
    {
        var declaring = compilation.GetTypeByMetadataName(declaringTypeName);
        var assemblies = ImmutableArray.CreateBuilder<IAssemblySymbol>();

        assemblies.Add(compilation.Assembly);

        if (declaring is not null)
        {
            assemblies.Add(declaring.ContainingAssembly);
        }

        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (declaring is null || References(reference, declaring.ContainingAssembly))
            {
                assemblies.Add(reference);
            }
        }

        return assemblies.ToImmutable();
    }

    /// <summary>Every assembly the compilation can see, which is what a failed narrowing falls back to.</summary>
    internal static ImmutableArray<IAssemblySymbol> Every(Compilation compilation) =>
        [compilation.Assembly, .. compilation.SourceModule.ReferencedAssemblySymbols];

    private static bool References(IAssemblySymbol assembly, IAssemblySymbol referenced) =>
        assembly.Modules.Any(module =>
            module.ReferencedAssemblySymbols.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, referenced)));

    /// <summary>Every type in a namespace or type, nested ones included.</summary>
    internal static IEnumerable<INamedTypeSymbol> Types(INamespaceOrTypeSymbol root, CancellationToken cancellationToken)
    {
        foreach (var member in root.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (member is INamespaceSymbol space)
            {
                foreach (var type in Types(space, cancellationToken))
                {
                    yield return type;
                }
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;

                foreach (var nested in Types(type, cancellationToken))
                {
                    yield return nested;
                }
            }
        }
    }
}

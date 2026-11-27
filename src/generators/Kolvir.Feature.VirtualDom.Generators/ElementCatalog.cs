using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Kolvir.Feature.VirtualDom.Generators;

/// <summary>
/// Every non-control element type marked <c>[VirtualizableElement]</c> that the compilation can see,
/// found once per compilation.
/// </summary>
/// <remarks>
/// <para>
/// The element counterpart of <see cref="ControlCatalog"/>. An element type qualifies when a
/// description class could be built from it: concrete, non-generic, accessible from the generated
/// code, constructible without arguments, and not itself a control. Everything else is reported,
/// because the attribute is an explicit declaration and silently ignoring it would leave a collection
/// property mysteriously absent.
/// </para>
/// <para>
/// The walk is narrowed to the assemblies that can name the attribute, which is every assembly that
/// references the one declaring it. That is complete by construction: an element type cannot carry
/// the attribute without being able to name it.
/// </para>
/// </remarks>
internal sealed record ElementCatalog(ImmutableArray<ElementModel> Elements, ImmutableArray<DiagnosticInfo> Reported)
{
    private const string AttributeName = "Kolvir.VirtualizableElementAttribute";
    private const string ControlTypeName = "Kolvir.Control";

    /// <summary>Nothing found, which is what a compilation with no marked elements gets.</summary>
    internal static ElementCatalog Empty => new(ImmutableArray<ElementModel>.Empty, ImmutableArray<DiagnosticInfo>.Empty);

    /// <summary>Finds every description class a collection property could need.</summary>
    /// <param name="compilation">The compilation whose assemblies are walked.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <returns>The elements, in name order so that one build always writes the same file.</returns>
    internal static ElementCatalog Sweep(Compilation compilation, CancellationToken cancellationToken)
    {
        var attribute = compilation.GetTypeByMetadataName(AttributeName);

        if (attribute is null)
        {
            return Empty;
        }

        var control = compilation.GetTypeByMetadataName(ControlTypeName);
        var found = ImmutableArray.CreateBuilder<ElementModel>();
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        foreach (var assembly in TypeWalk.Around(compilation, AttributeName))
        {
            foreach (var type in TypeWalk.Types(assembly.GlobalNamespace, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!HasAttribute(type, attribute))
                {
                    continue;
                }

                if (!Eligible(type, control, compilation))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        Diagnostics.ElementIsNotUsable,
                        type.Locations.FirstOrDefault() ?? Location.None,
                        [type.ToDisplayString()]));
                    continue;
                }

                found.Add(new ElementModel(
                    "V" + type.Name,
                    type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
                    type.ToDisplayString(Formats.Type),
                    type.ToDisplayString(Formats.Metadata),
                    VirtualDomGenerator.ReadProperties(
                        type,
                        type.Locations.FirstOrDefault() ?? Location.None,
                        diagnostics,
                        collections: false).Where(property => !property.SlotBorrows).ToImmutableArray(),
                    type.Locations.FirstOrDefault() ?? Location.None));
            }
        }

        found.Sort(static (left, right) => string.CompareOrdinal(left.ElementMetadataName, right.ElementMetadataName));

        return new ElementCatalog(found.ToImmutable(), diagnostics.ToImmutable());
    }

    private static bool HasAttribute(INamedTypeSymbol type, INamedTypeSymbol attribute) =>
        type.GetAttributes().Any(candidate =>
            SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attribute));

    private static bool Eligible(INamedTypeSymbol type, INamedTypeSymbol? control, Compilation compilation) =>
        type.TypeKind == TypeKind.Class
        && !type.IsAbstract
        && !type.IsStatic
        && type.Arity == 0
        && (control is null || !Derives(type, control))
        && Accessible(type.DeclaredAccessibility, type, compilation)
        && type.InstanceConstructors.Any(constructor =>
            constructor.Parameters.Length == 0 && Accessible(constructor.DeclaredAccessibility, type, compilation));

    private static bool Accessible(Accessibility accessibility, INamedTypeSymbol owner, Compilation compilation) =>
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

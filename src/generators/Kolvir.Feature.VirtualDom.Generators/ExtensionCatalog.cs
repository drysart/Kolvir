using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kolvir.Feature.VirtualDom.Generators;

/// <summary>The symbol display formats the generator and the scan share.</summary>
internal static class Formats
{
    /// <summary>A type as generated code has to write it: fully qualified, nullable annotations kept.</summary>
    internal static readonly SymbolDisplayFormat Type = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>A type the way a reader names it: <c>Kolvir.Control</c> rather than <c>global::Kolvir.Control</c>.</summary>
    internal static readonly SymbolDisplayFormat Metadata = SymbolDisplayFormat.CSharpErrorMessageFormat;
}

/// <summary>Whether a name can be written as a C# identifier.</summary>
internal static class Names
{
    internal static bool IsIdentifier(string name)
    {
        if (name.Length == 0 || !SyntaxFacts.IsIdentifierStartCharacter(name[0]))
        {
            return false;
        }

        for (int i = 1; i < name.Length; i++)
        {
            if (!SyntaxFacts.IsIdentifierPartCharacter(name[i]))
            {
                return false;
            }
        }

        return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None;
    }
}

/// <summary>
/// One extension member - a property, or the pair of methods behind an event - that was found in a
/// referenced assembly and could be exposed on a node.
/// </summary>
/// <param name="Container">The static class declaring it, fully qualified, as generated code writes it.</param>
/// <param name="ContainerMetadataName">The same class the way a reader names it, for diagnostics and overrides.</param>
/// <param name="Name">The property's name, or the event name the <c>Add</c>/<c>Remove</c> pair implies.</param>
/// <param name="Type">The property's type or the handler delegate's, fully qualified and nullable.</param>
/// <param name="AddHandler">The handler-adding method, when this member is an event.</param>
/// <param name="RemoveHandler">The handler-removing method, when this member is an event.</param>
/// <param name="Receiver">The receiver type the way a reader names it, for diagnostics.</param>
/// <param name="ReceiverChain">The receiver and its base classes, so a control can be compared against it.</param>
/// <param name="ReceiverInterfaces">The interfaces the receiver implements, for the same reason.</param>
/// <param name="UnsetLiteral">The pristine value a <c>[VNodePropertyUnsetValue]</c> declared, as a C# literal, or null.</param>
internal sealed record ExtensionMember(
    string Container,
    string ContainerMetadataName,
    string Name,
    string Type,
    string? AddHandler,
    string? RemoveHandler,
    string Receiver,
    ImmutableArray<string> ReceiverChain,
    ImmutableArray<string> ReceiverInterfaces,
    string? UnsetLiteral)
{
    internal bool IsEvent => AddHandler is not null;

    /// <summary>How the member is named in a diagnostic: its container and its own name.</summary>
    internal string Contender => ContainerMetadataName + "::" + Name;
}

/// <summary>
/// Every extension member the compilation can see that could reach a control, found once per
/// compilation and filtered per control.
/// </summary>
/// <remarks>
/// <para>
/// The scan is narrowed to the assemblies it can be narrowed to: the compilation's own assembly, the
/// assembly that declares <c>Control</c>, and the referenced assemblies that reference that one. That
/// is complete by construction rather than by heuristic - to write <c>extension(Control control)</c>
/// an assembly has to be able to name <c>Control</c>, so it has to reference the assembly that
/// declares it - and the spike measured it at under a millisecond against about twenty for a walk of
/// every referenced type (see <c>extension-members-from-roslyn</c> in <c>dev-docs</c>).
/// </para>
/// <para>
/// The one hole the construction has is type forwarding, where a receiver resolves through a forwarder
/// the narrowed set does not include. A block whose receiver does not resolve to a type is exactly
/// that case, and it widens the scan to every referenced assembly rather than silently missing the
/// members it could not read.
/// </para>
/// <para>
/// The scan is a unit of its own rather than part of the manifest walk because a future
/// <c>[VirtualDomAll]</c> needs the same index over every control the compilation can see.
/// </para>
/// </remarks>
internal sealed record ExtensionCatalog(ImmutableArray<ExtensionMember> Members, bool WasWidened)
{
    private const string ControlTypeName = "Kolvir.Control";

    /// <summary>Nothing found, which is what a compilation with no extension members gets.</summary>
    internal static ExtensionCatalog Empty => new(ImmutableArray<ExtensionMember>.Empty, false);

    /// <summary>Finds every extension member in the assemblies the compilation can draw one from.</summary>
    /// <param name="compilation">The compilation whose references are scanned.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <returns>The members found, and whether the narrowing had to be given up.</returns>
    internal static ExtensionCatalog Scan(Compilation compilation, CancellationToken cancellationToken)
    {
        var control = compilation.GetTypeByMetadataName(ControlTypeName);
        var members = ImmutableArray.CreateBuilder<ExtensionMember>();

        if (!Read(Narrowed(compilation, control), members, cancellationToken))
        {
            return new ExtensionCatalog(members.ToImmutable(), false);
        }

        // A receiver that resolved to nothing means the narrowed set was missing an assembly, which is
        // the one thing the construction above can be wrong about. Everything is read instead.
        members.Clear();
        Read(Widened(compilation), members, cancellationToken);

        return new ExtensionCatalog(members.ToImmutable(), true);
    }

    /// <summary>The assemblies a member can be declared in, given that naming the receiver takes a reference.</summary>
    private static ImmutableArray<IAssemblySymbol> Narrowed(Compilation compilation, INamedTypeSymbol? control)
    {
        var declaring = control?.ContainingAssembly;
        var assemblies = ImmutableArray.CreateBuilder<IAssemblySymbol>();

        assemblies.Add(compilation.Assembly);

        if (declaring is not null)
        {
            assemblies.Add(declaring);
        }

        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (declaring is null || References(reference, declaring))
            {
                assemblies.Add(reference);
            }
        }

        return assemblies.ToImmutable();
    }

    /// <summary>Every assembly the compilation can see, which is what a failed narrowing falls back to.</summary>
    private static ImmutableArray<IAssemblySymbol> Widened(Compilation compilation) =>
        [compilation.Assembly, .. compilation.SourceModule.ReferencedAssemblySymbols];

    private static bool References(IAssemblySymbol assembly, IAssemblySymbol referenced) =>
        assembly.Modules.Any(module =>
            module.ReferencedAssemblySymbols.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, referenced)));

    /// <summary>Reads one set of assemblies. Returns whether a receiver failed to resolve.</summary>
    private static bool Read(
        ImmutableArray<IAssemblySymbol> assemblies,
        ImmutableArray<ExtensionMember>.Builder members,
        CancellationToken cancellationToken)
    {
        var unresolved = false;

        foreach (var assembly in assemblies)
        {
            foreach (var type in Types(assembly.GlobalNamespace, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var member in type.GetMembers())
                {
                    if (member is INamedTypeSymbol { TypeKind: TypeKind.Extension } block)
                    {
                        unresolved |= ReadBlock(type, block, members);
                    }
                }

                unresolved |= ReadHandlerPairs(type, members);
            }
        }

        return unresolved;
    }

    /// <summary>Reads the properties of one <c>extension(...)</c> block. Returns whether its receiver failed to resolve.</summary>
    private static bool ReadBlock(
        INamedTypeSymbol container,
        INamedTypeSymbol block,
        ImmutableArray<ExtensionMember>.Builder members)
    {
        // The block's receiver: the parameter the compiler modelled the block on. It is the reason the
        // pin is on 5.x - 4.14 has no such API and cannot see these members in metadata at all.
        var receiver = block.ExtensionParameter?.Type;

        if (receiver is null || receiver.TypeKind is TypeKind.Error)
        {
            return true;
        }

        foreach (var property in block.GetMembers().OfType<IPropertySymbol>())
        {
            // The same filter the native walk applies, for the same reason: a node can only set what a
            // caller could set. The getter is required because the node reads the pristine value back
            // through it when a render drops the property.
            if (property.IsStatic
                || property.IsIndexer
                || property.IsAbstract
                || property.GetMethod?.DeclaredAccessibility != Accessibility.Public
                || property.SetMethod?.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            members.Add(Member(container, receiver, property.Name, property.Type, null, null, Values.Unset(property)));
        }

        return false;
    }

    /// <summary>Reads the flattened extension methods of one container, which is where handler pairs live.</summary>
    /// <remarks>
    /// Extension methods declared inside an <c>extension</c> block reach the container as ordinary static
    /// methods with the receiver as their first parameter, exactly like a classic extension method, and
    /// both report <see cref="IMethodSymbol.IsExtensionMethod"/>. So the pair is found without reading a
    /// block symbol at all, which is what makes this half work the same way on every Roslyn version.
    /// </remarks>
    private static bool ReadHandlerPairs(INamedTypeSymbol container, ImmutableArray<ExtensionMember>.Builder members)
    {
        var unresolved = false;
        var methods = container.GetMembers().OfType<IMethodSymbol>()
            .Where(method => method.IsStatic && method.IsExtensionMethod && method.DeclaredAccessibility == Accessibility.Public)
            .ToList();

        foreach (var add in methods)
        {
            if (add.Parameters.Length != 2
                || !add.Name.StartsWith("Add", StringComparison.Ordinal)
                || !add.Name.EndsWith("Handler", StringComparison.Ordinal))
            {
                continue;
            }

            var kind = add.Name.Substring(3, add.Name.Length - 3 - 7);

            if (!Names.IsIdentifier(kind))
            {
                continue;
            }

            var handler = add.Parameters[1].Type;

            if (handler.TypeKind != TypeKind.Delegate)
            {
                continue;
            }

            var receiver = add.Parameters[0].Type;

            if (receiver.TypeKind is TypeKind.Error)
            {
                unresolved = true;
                continue;
            }

            var remove = methods.FirstOrDefault(method =>
                method.Name == "Remove" + kind + "Handler"
                && method.Parameters.Length == 2
                && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, receiver)
                && SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, handler));

            if (remove is null)
            {
                continue;
            }

            var display = handler.ToDisplayString(Formats.Type);

            members.Add(new ExtensionMember(
                container.ToDisplayString(Formats.Type),
                container.ToDisplayString(Formats.Metadata),
                kind,
                display.EndsWith("?", StringComparison.Ordinal) ? display : display + "?",
                add.Name,
                remove.Name,
                receiver.ToDisplayString(Formats.Metadata),
                Chain(receiver),
                [.. receiver.AllInterfaces.Select(@interface => @interface.ToDisplayString(Formats.Metadata))],
                UnsetLiteral: null));
        }

        return unresolved;
    }

    private static ExtensionMember Member(
        INamedTypeSymbol container,
        ITypeSymbol receiver,
        string name,
        ITypeSymbol type,
        string? addHandler,
        string? removeHandler,
        string? unsetLiteral = null) =>
        new(
            container.ToDisplayString(Formats.Type),
            container.ToDisplayString(Formats.Metadata),
            name,
            type.ToDisplayString(Formats.Type),
            addHandler,
            removeHandler,
            receiver.ToDisplayString(Formats.Metadata),
            Chain(receiver),
            [.. receiver.AllInterfaces.Select(@interface => @interface.ToDisplayString(Formats.Metadata))],
            unsetLiteral);

    /// <summary>The receiver and the base classes it can be applied to, in the order they are searched.</summary>
    private static ImmutableArray<string> Chain(ITypeSymbol receiver)
    {
        var chain = ImmutableArray.CreateBuilder<string>();

        for (var type = receiver; type is not null; type = type.BaseType)
        {
            chain.Add(type.ToDisplayString(Formats.Metadata));
        }

        return chain.ToImmutable();
    }

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceOrTypeSymbol root, CancellationToken cancellationToken)
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

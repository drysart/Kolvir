using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kolvir.VirtualDom.Generators;

/// <summary>
/// Turns <c>[VirtualDom]</c> registrations into node classes.
/// </summary>
/// <remarks>
/// <para>
/// The generator reads the manifest at compile time, which is the whole reason the virtual DOM is
/// trim-safe: it sees the exact control type from a <c>typeof</c>, so it can emit direct property
/// assignments. Nothing here reflects, and no member of the manifest survives into the build.
/// </para>
/// <para>
/// One file per manifest, each holding one class per registered control. The classes are emitted into
/// the manifest's namespace so that a view only needs `using` that namespace.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class VirtualDomGenerator : IIncrementalGenerator
{
    private const string AttributeName = "Kolvir.VirtualDom.VirtualDomAttribute";
    private const string ControlTypeName = "Kolvir.Control";

    /// <summary>
    /// The members the node base class and the generated body already have, which a flattened
    /// property cannot share.
    /// </summary>
    private static readonly string[] ReservedNames =
        ["Key", "Children", "NodeType", "ApplyTo", "PatchFrom", "Kind", "Pristine"];

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var manifests = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (syntaxContext, cancellationToken) => Read(syntaxContext, cancellationToken))
            .Collect();

        context.RegisterSourceOutput(manifests, static (productionContext, models) => Emit(productionContext, models));
    }

    private static ManifestModel? Read(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not INamedTypeSymbol manifest)
        {
            return null;
        }

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var controls = ImmutableArray.CreateBuilder<ControlModel>();

        if (!IsPartial(manifest))
        {
            diagnostics.Add(Info(
                Diagnostics.ManifestIsNotPartial,
                context.Attributes[0].ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation(),
                manifest.Name));
        }

        var compilation = context.SemanticModel.Compilation;
        var controlSymbol = compilation.GetTypeByMetadataName(ControlTypeName);

        foreach (var attribute in context.Attributes)
        {
            var location = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
                ?? manifest.Locations.FirstOrDefault()
                ?? Location.None;

            if (attribute.ConstructorArguments.Length == 0
                || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol registered)
            {
                continue;
            }

            var name = registered.Name;

            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key == "Name" && argument.Value.Value is string given)
                {
                    name = given;
                }
            }

            if (!IsIdentifier(name))
            {
                diagnostics.Add(Info(Diagnostics.NameIsNotAnIdentifier, location, name));
                continue;
            }

            if (controlSymbol is null || !InheritsFrom(registered, controlSymbol))
            {
                diagnostics.Add(Info(Diagnostics.NotAControl, location, registered.ToDisplayString()));
                continue;
            }

            if (registered.IsAbstract || !HasParameterlessConstructor(registered))
            {
                diagnostics.Add(Info(Diagnostics.NotConstructible, location, registered.ToDisplayString()));
                continue;
            }

            var nodeName = "V" + name;
            if (controls.Any(c => c.NodeName == nodeName))
            {
                diagnostics.Add(Info(Diagnostics.DuplicateNodeName, location, nodeName));
                continue;
            }

            controls.Add(new ControlModel(
                nodeName,
                registered.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
                registered.ToDisplayString(TypeFormat),
                AcceptsChildren(registered),
                ReadProperties(registered, location, diagnostics),
                location));
        }

        return new ManifestModel(
            manifest.Name,
            manifest.ContainingNamespace.IsGlobalNamespace ? null : manifest.ContainingNamespace.ToDisplayString(),
            controls.ToImmutable(),
            diagnostics.ToImmutable());
    }

    private static ImmutableArray<PropertyModel> ReadProperties(
        INamedTypeSymbol control,
        Location location,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        var properties = ImmutableArray.CreateBuilder<PropertyModel>();
        var taken = new List<string>(ReservedNames);

        foreach (var property in SettableProperties(control))
        {
            if (ReservedNames.Contains(property.Name))
            {
                diagnostics.Add(Info(Diagnostics.MemberNameCollides, location, control.Name + "." + property.Name, "the base node class"));
                continue;
            }

            // A name already taken is a derived property hiding a base one, which is the property a
            // caller sees and which the walk reached first, so the base declaration is dropped in
            // silence: hiding is a normal thing for a control to do, not a mistake to report.
            if (taken.Contains(property.Name))
            {
                continue;
            }

            taken.Add(property.Name);
            properties.Add(new PropertyModel(
                property.Name,
                property.Type.ToDisplayString(TypeFormat),
                "_" + Lower(property.Name),
                "Pristine" + property.Name,
                AddHandler: null,
                RemoveHandler: null));
        }

        foreach (var (name, add, remove, handlerType) in HandlerPairs(control))
        {
            if (taken.Contains(name))
            {
                diagnostics.Add(Info(Diagnostics.MemberNameCollides, location, control.Name + "." + name, "another generated member"));
                continue;
            }

            taken.Add(name);
            properties.Add(new PropertyModel(
                name,
                handlerType,
                "_" + Lower(name),
                Pristine: null,
                AddHandler: add,
                RemoveHandler: remove));
        }

        return properties.ToImmutable();
    }

    private static IEnumerable<IPropertySymbol> SettableProperties(INamedTypeSymbol control)
    {
        for (var type = control; type is not null; type = type.BaseType)
        {
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic
                    || property.IsIndexer
                    || property.IsAbstract
                    || property.GetMethod is not { DeclaredAccessibility: Accessibility.Public }
                    || property.SetMethod is not { DeclaredAccessibility: Accessibility.Public })
                {
                    continue;
                }

                yield return property;
            }
        }
    }

    /// <summary>
    /// Finds the <c>AddXxxHandler</c>/<c>RemoveXxxHandler</c> pairs Kolvir uses instead of events.
    /// </summary>
    private static IEnumerable<(string Name, string Add, string Remove, string HandlerType)> HandlerPairs(
        INamedTypeSymbol control)
    {
        var methods = new List<IMethodSymbol>();

        for (var type = control; type is not null; type = type.BaseType)
        {
            methods.AddRange(type.GetMembers().OfType<IMethodSymbol>());
        }

        foreach (var add in methods)
        {
            if (add.IsStatic
                || add.DeclaredAccessibility != Accessibility.Public
                || !add.Name.StartsWith("Add", StringComparison.Ordinal)
                || !add.Name.EndsWith("Handler", StringComparison.Ordinal)
                || add.Parameters.Length != 1)
            {
                continue;
            }

            var kind = add.Name.Substring(3, add.Name.Length - 3 - 7);

            if (kind.Length == 0 || !IsIdentifier(kind))
            {
                continue;
            }

            var handlerType = add.Parameters[0].Type;

            if (handlerType.TypeKind != TypeKind.Delegate)
            {
                continue;
            }

            var remove = methods.FirstOrDefault(m =>
                m.Name == "Remove" + kind + "Handler"
                && m.Parameters.Length == 1
                && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, handlerType));

            if (remove is null)
            {
                continue;
            }

            var display = handlerType.ToDisplayString(TypeFormat);

            yield return (kind, add.Name, remove.Name, display.EndsWith("?", StringComparison.Ordinal) ? display : display + "?");
        }
    }

    /// <summary>
    /// Whether a control arranges children, which is what makes its node a container rather than a leaf.
    /// </summary>
    /// <remarks>
    /// A container's answer to "where do my children go" is its <c>OnLayout</c> override - that is
    /// the method <c>Control</c> calls on an arrange pass and that every container type in the
    /// library overrides. So a type that does not override it anywhere in its chain puts its children
    /// nowhere, and a node for it carries <c>[LeafNode]</c> for the analyzer to find.
    /// </remarks>
    private static bool AcceptsChildren(INamedTypeSymbol control)
    {
        for (var type = control; type is not null && type.SpecialType == SpecialType.None; type = type.BaseType)
        {
            foreach (var method in type.GetMembers("OnLayout").OfType<IMethodSymbol>())
            {
                if (method.IsOverride && method.Parameters.Length == 0 && method.ReturnsVoid)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsPartial(INamedTypeSymbol manifest) =>
        manifest.DeclaringSyntaxReferences.Any(
            reference => reference.GetSyntax() is ClassDeclarationSyntax declaration
                && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static bool HasParameterlessConstructor(INamedTypeSymbol control) =>
        control.InstanceConstructors.Any(
            constructor => constructor.Parameters.Length == 0
                && constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal);

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIdentifier(string name)
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

    private static string Lower(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    private static DiagnosticInfo Info(DiagnosticDescriptor descriptor, Location? location, params string[] arguments) =>
        new(descriptor, location ?? Location.None, arguments.ToImmutableArray());

    private static void Emit(SourceProductionContext context, ImmutableArray<ManifestModel?> models)
    {
        var seenTypes = new Dictionary<string, Location>();

        foreach (var model in models)
        {
            if (model is null)
            {
                continue;
            }

            foreach (var diagnostic in model.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
            }

            foreach (var control in model.Controls)
            {
                if (seenTypes.TryGetValue(control.ControlType, out var first))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.DuplicateRegistration,
                        control.Location,
                        new[] { first }.Cast<object>().Concat(new object[] { control.ControlType }).ToArray()));
                }
                else
                {
                    seenTypes.Add(control.ControlType, control.Location);
                }
            }
        }

        foreach (var model in models)
        {
            if (model is null || model.Controls.IsEmpty)
            {
                continue;
            }

            var source = new StringBuilder();
            NodeEmitter.Write(model, source);

            var hint = (model.Namespace is null ? "Global" : model.Namespace.Replace('.', '_'))
                + "." + model.Name + ".VirtualDom.g.cs";
            context.AddSource(hint, source.ToString());
        }
    }
}

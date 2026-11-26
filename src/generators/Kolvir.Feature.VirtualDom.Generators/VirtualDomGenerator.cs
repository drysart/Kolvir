using System.Collections.Immutable;

using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kolvir.Feature.VirtualDom.Generators;

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
/// <para>
/// A control's node also exposes the C# 14 extension members that apply to it: the properties and
/// handler pairs an <c>extension(...)</c> block or a static class adds to a type the control is
/// assignable to. <see cref="ExtensionCatalog"/> finds them once per compilation; this class decides,
/// per control, which name wins and which becomes a placeholder. The design is in
/// <c>dev-docs/EXTENSION-MEMBERS-ON-VNODES.md</c>.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class VirtualDomGenerator : IIncrementalGenerator
{
    private const string AttributeName = "Kolvir.Feature.VirtualDom.VirtualDomAttribute";
    private const string SweepAttributeName = "Kolvir.Feature.VirtualDom.VirtualDomAllAttribute";
    private const string OverrideAttributeName = "Kolvir.Feature.VirtualDom.VNodeExtensionPropertyNameAttribute";
    private const string UnsetValueAttributeName = "Kolvir.Feature.VirtualDom.VNodePropertyUnsetValueAttribute";
    private const string ControlTypeName = "Kolvir.Control";

    /// <summary>
    /// The members the node base class and the generated body already have, which a flattened
    /// property cannot share.
    /// </summary>
    private static readonly string[] ReservedNames =
        ["Key", "Children", "NodeType", "ApplyTo", "PatchFrom", "Kind", "Pristine", "Extensions", "ApplyExtensionActuators"];

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // The reference documentation the build hands in (see the feature's buildTransitive targets).
        // One index for the compilation: which file documents which assembly does not depend on the
        // compilation, and parsing stays deferred until a member is asked about.
        var documentation = context.AdditionalTextsProvider
            .Collect()
            .Select(static (texts, _) => DocumentationIndex.Create(texts));

        var manifests = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (syntaxContext, cancellationToken) => Read(syntaxContext, cancellationToken))
            .Collect();

        var sweeps = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SweepAttributeName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (syntaxContext, cancellationToken) => ReadSweep(syntaxContext, cancellationToken))
            .Collect();

        // Once per compilation rather than once per manifest: both walks are the expensive half of this
        // generator, and their answers are the same for every manifest the compilation declares.
        var catalog = context.CompilationProvider
            .Combine(documentation)
            .Select(static (input, cancellationToken) => ExtensionCatalog.Scan(input.Left, input.Right, cancellationToken));

        // The sweep's half of the same discipline: what a [VirtualDomAll] manifest has to emit for.
        // Only walked when a manifest actually sweeps - a compilation that registers its controls by
        // name has no use for it, and the walk is half a millisecond it should not pay.
        var controls = sweeps.Combine(context.CompilationProvider)
            .Select(static (input, cancellationToken) =>
                input.Left.Any(model => model is not null)
                    ? ControlCatalog.Sweep(input.Right, cancellationToken)
                    : ControlCatalog.Empty);

        context.RegisterSourceOutput(
            manifests.Combine(sweeps).Combine(catalog).Combine(controls).Combine(documentation),
            static (productionContext, input) => Emit(
                productionContext,
                input.Left.Left.Left.Left,
                input.Left.Left.Left.Right,
                input.Left.Left.Right,
                input.Left.Right,
                input.Right));
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

            if (!Names.IsIdentifier(name))
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

            controls.Add(Model(registered, name, location, diagnostics));
        }

        return new ManifestModel(
            manifest.Name,
            manifest.ContainingNamespace.IsGlobalNamespace ? null : manifest.ContainingNamespace.ToDisplayString(),
            controls.ToImmutable(),
            ImmutableArray<TypePlaceholderModel>.Empty,
            ReadOverrides(compilation),
            diagnostics.ToImmutable(),
            manifest.Locations.FirstOrDefault() ?? Location.None);
    }

    /// <summary>
    /// Reads a <c>[VirtualDomAll]</c> manifest, which names nothing: what it sweeps is added when the
    /// output is produced, because that list is the compilation's rather than the manifest's.
    /// </summary>
    private static ManifestModel? ReadSweep(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not INamedTypeSymbol manifest)
        {
            return null;
        }

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        if (!IsPartial(manifest))
        {
            diagnostics.Add(Info(
                Diagnostics.ManifestIsNotPartial,
                context.Attributes[0].ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation(),
                manifest.Name));
        }

        return new ManifestModel(
            manifest.Name,
            manifest.ContainingNamespace.IsGlobalNamespace ? null : manifest.ContainingNamespace.ToDisplayString(),
            ImmutableArray<ControlModel>.Empty,
            ImmutableArray<TypePlaceholderModel>.Empty,
            ReadOverrides(context.SemanticModel.Compilation),
            diagnostics.ToImmutable(),
            manifest.Locations.FirstOrDefault() ?? Location.None,
            Sweeps: true);
    }

    /// <summary>The node model for one control. Registration and a sweep differ only in how the control was named.</summary>
    private static ControlModel Model(
        INamedTypeSymbol control,
        string name,
        Location location,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics) =>
        new(
            "V" + name,
            control.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
            control.ToDisplayString(Formats.Type),
            control.ToDisplayString(Formats.Metadata),
            AcceptsChildren(control),
            ReadProperties(control, location, diagnostics),
            ImmutableArray<MemberPlaceholderModel>.Empty,
            Chain(control),
            Interfaces(control),
            location);

    private static ImmutableArray<PropertyModel> ReadProperties(
        INamedTypeSymbol control,
        Location location,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        var properties = ImmutableArray.CreateBuilder<PropertyModel>();

        // Whatever a name was taken by, so that a name met twice can be told apart from a name taken by
        // something else: a hidden base property and an inherited handler pair are both the same member
        // reached twice, while a property and an event with one name is a collision.
        var taken = new Dictionary<string, bool>(StringComparer.Ordinal);

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
            if (taken.ContainsKey(property.Name))
            {
                continue;
            }

            taken.Add(property.Name, false);
            properties.Add(new PropertyModel(
                property.Name,
                property.Type.ToDisplayString(Formats.Type),
                "_" + Lower(property.Name),
                "Pristine" + property.Name,
                Values.Unset(property),
                AddHandler: null,
                RemoveHandler: null,
                Container: null,
                Accessor: property.Name,
                Documentation: DocumentationCleaner.Clean(property.GetDocumentationCommentXml()),
                DocumentationId: property.GetDocumentationCommentId(),
                DocumentationAssembly: property.ContainingAssembly?.Name));
        }

        foreach (var (name, add, remove, handlerType, documentation, documentationId, documentationAssembly) in HandlerPairs(control))
        {
            if (ReservedNames.Contains(name))
            {
                diagnostics.Add(Info(Diagnostics.MemberNameCollides, location, control.Name + "." + name, "the base node class"));
                continue;
            }

            if (taken.TryGetValue(name, out var isEvent))
            {
                // An inherited or hidden handler pair is the same event reached twice; a property that
                // already owns the name is a real collision, and losing the event silently would be
                // worse than an error.
                if (!isEvent)
                {
                    diagnostics.Add(Info(Diagnostics.MemberNameCollides, location, control.Name + "." + name, "another generated member"));
                }

                continue;
            }

            taken.Add(name, true);
            properties.Add(new PropertyModel(
                name,
                handlerType,
                "_" + Lower(name),
                Pristine: null,
                PristineLiteral: null,
                AddHandler: add,
                RemoveHandler: remove,
                Container: null,
                Accessor: name,
                Documentation: documentation,
                DocumentationId: documentationId,
                DocumentationAssembly: documentationAssembly));
        }

        return properties.ToImmutable();
    }

    /// <summary>
    /// Adds the extension members that apply to <paramref name="control"/>, resolving every name to one
    /// exposable member or to an inert placeholder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rules are the language's own. A member of the control wins over an extension member of the
    /// same name, because C# resolves an instance member before it looks at an extension. A member on a
    /// more specific receiver wins over one on a base, exactly as a derived property hides a base one;
    /// the catalogue is ordered per control by how specific each receiver is, so that is the order the
    /// members are met in. What is left - two containers extending the same receiver with the same name
    /// - cannot be resolved by choosing without choosing silently, so the name becomes a placeholder
    /// and the analyzer reports its use.
    /// </para>
    /// <para>
    /// An override wins over the member's own name, and one that lands on a name the control already
    /// gives the node is reported (VD015) rather than placed in a conflict, because then the
    /// application has to say which of the two it meant.
    /// </para>
    /// </remarks>
    private static ControlModel Resolve(
        ControlModel control,
        ExtensionCatalog catalog,
        ImmutableArray<OverrideModel> overrides,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (catalog.Members.IsEmpty)
        {
            return control;
        }

        var claims = new List<Claim>(control.Properties.Length + catalog.Members.Length);
        var order = new List<string>(control.Properties.Length + catalog.Members.Length);

        foreach (var property in control.Properties)
        {
            claims.Add(Claim.ForNative(property));
            order.Add(property.Name);
        }

        var applicable = catalog.Members
            .Select(member => (Member: member, Rank: Rank(control, member)))
            .Where(entry => entry.Rank < int.MaxValue)
            .OrderBy(entry => entry.Rank)
            .ThenBy(entry => entry.Member.ContainerMetadataName, StringComparer.Ordinal)
            .ThenBy(entry => entry.Member.Name, StringComparer.Ordinal);

        foreach (var (member, _) in applicable)
        {
            var overridden = FindOverride(member, overrides);
            var name = overridden?.Name ?? member.Name;

            if (!Names.IsIdentifier(name))
            {
                diagnostics.Add(Info(Diagnostics.NameIsNotAnIdentifier, overridden?.Location ?? control.Location, name));
                continue;
            }

            if (ReservedNames.Contains(name))
            {
                diagnostics.Add(Info(Diagnostics.MemberNameCollides, control.Location, member.Contender, "the base node class"));
                continue;
            }

            var claim = claims.FirstOrDefault(candidate => candidate.Name == name);

            if (claim is null)
            {
                claims.Add(Claim.ForExtension(name, member));
                order.Add(name);
                continue;
            }

            if (claim.IsNative)
            {
                if (overridden is not null)
                {
                    diagnostics.Add(Info(
                        Diagnostics.ExtensionNameIsTaken,
                        overridden.Location,
                        member.ContainerMetadataName + "." + member.Name,
                        name,
                        control.ControlMetadataName + "." + name));
                }

                continue;
            }

            claim.Add(member);
        }

        var properties = ImmutableArray.CreateBuilder<PropertyModel>();
        var placeholders = ImmutableArray.CreateBuilder<MemberPlaceholderModel>();

        foreach (var name in order)
        {
            var claim = claims.First(candidate => candidate.Name == name);

            if (claim.Holds > 1)
            {
                placeholders.Add(new MemberPlaceholderModel(name, [.. claim.Contenders]));
            }
            else
            {
                properties.Add(claim.PropertyFor(name));
            }
        }

        return control with { Properties = properties.ToImmutable(), Placeholders = placeholders.ToImmutable() };
    }

    /// <summary>
    /// Fills in the documentation of every member whose text has to come from a reference's XML file.
    /// </summary>
    /// <remarks>
    /// A member declared in the compilation being built carries its own documentation and was read
    /// while the model was built. A member of a referenced assembly does not, so it is looked up here,
    /// by documentation ID in that assembly's file - which is why this runs at emit time: the model
    /// walk has no access to the additional files, and the sweep's models are built here anyway.
    /// </remarks>
    private static ControlModel Documentate(ControlModel control, DocumentationIndex documentation)
    {
        if (!control.Properties.Any(property => property.Documentation is null && property.DocumentationId is not null))
        {
            return control;
        }

        var properties = ImmutableArray.CreateBuilder<PropertyModel>(control.Properties.Length);

        foreach (var property in control.Properties)
        {
            properties.Add(
                property.Documentation is null
                    && property.DocumentationId is { } id
                    && property.DocumentationAssembly is { } assembly
                    && documentation.MemberOf(assembly, id) is { } text
                    ? property with { Documentation = text }
                    : property);
        }

        return control with { Properties = properties.ToImmutable() };
    }

    private static OverrideModel? FindOverride(ExtensionMember member, ImmutableArray<OverrideModel> overrides)
    {
        foreach (var candidate in overrides)
        {
            if (candidate.Container == member.ContainerMetadataName && candidate.Member == member.Name)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// How specific a member's receiver is for this control: its distance in the control's own chain,
    /// and after every class, the interfaces. A receiver the control is not assignable to is not
    /// applicable at all.
    /// </summary>
    private static int Rank(ControlModel control, ExtensionMember member)
    {
        var index = control.Chain.IndexOf(member.Receiver);

        if (index >= 0)
        {
            return index;
        }

        return control.Interfaces.Contains(member.Receiver) ? control.Chain.Length : int.MaxValue;
    }

    private static ImmutableArray<string> Chain(INamedTypeSymbol control)
    {
        var chain = ImmutableArray.CreateBuilder<string>();

        for (var type = control; type is not null; type = type.BaseType)
        {
            chain.Add(type.ToDisplayString(Formats.Metadata));
        }

        return chain.ToImmutable();
    }

    private static ImmutableArray<string> Interfaces(INamedTypeSymbol control) =>
        [.. control.AllInterfaces.Select(@interface => @interface.ToDisplayString(Formats.Metadata))];

    private static ImmutableArray<OverrideModel> ReadOverrides(Compilation compilation)
    {
        var overrides = ImmutableArray.CreateBuilder<OverrideModel>();

        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != OverrideAttributeName
                || attribute.ConstructorArguments.Length != 3
                || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol container
                || attribute.ConstructorArguments[1].Value is not string member
                || attribute.ConstructorArguments[2].Value is not string name)
            {
                continue;
            }

            overrides.Add(new OverrideModel(
                container.ToDisplayString(Formats.Metadata),
                member,
                name,
                attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None));
        }

        return overrides.ToImmutable();
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
    private static IEnumerable<(string Name, string Add, string Remove, string HandlerType, string? Documentation, string? DocumentationId, string? DocumentationAssembly)> HandlerPairs(
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

            if (!Names.IsIdentifier(kind))
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

            var display = handlerType.ToDisplayString(Formats.Type);

            yield return (
                kind,
                add.Name,
                remove.Name,
                display.EndsWith("?", StringComparison.Ordinal) ? display : display + "?",
                DocumentationCleaner.Clean(add.GetDocumentationCommentXml()),
                add.GetDocumentationCommentId(),
                add.ContainingAssembly?.Name);
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

    private static string Lower(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    private static DiagnosticInfo Info(DiagnosticDescriptor descriptor, Location? location, params string[] arguments) =>
        new(descriptor, location ?? Location.None, arguments.ToImmutableArray());

    private static void Emit(
        SourceProductionContext context,
        ImmutableArray<ManifestModel?> models,
        ImmutableArray<ManifestModel?> sweeps,
        ExtensionCatalog catalog,
        ControlCatalog controls,
        DocumentationIndex documentation)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        // The explicit manifests come first, then the sweeping ones, because the order of the claims
        // below is the order of the generated file: a control the application named keeps its place at
        // the top, and what a sweep adds follows it.
        var manifests = new List<ManifestModel>(models.Length + sweeps.Length);

        foreach (var model in models)
        {
            if (model is not null)
            {
                manifests.Add(model);
                diagnostics.AddRange(model.Diagnostics);
            }
        }

        foreach (var model in sweeps)
        {
            if (model is not null)
            {
                manifests.Add(model);
                diagnostics.AddRange(model.Diagnostics);
            }
        }

        // A class can carry both attributes: the controls it names and the ones it sweeps are one
        // manifest, and a second file for the same class would be a duplicate hint name. So a sweeping
        // manifest writes into the manifest of the class it was declared on.
        var owner = new int[manifests.Count];

        for (int index = 0; index < manifests.Count; index++)
        {
            owner[index] = index;

            if (!manifests[index].Sweeps)
            {
                continue;
            }

            for (int candidate = 0; candidate < manifests.Count; candidate++)
            {
                if (!manifests[candidate].Sweeps
                    && manifests[candidate].Name == manifests[index].Name
                    && manifests[candidate].Namespace == manifests[index].Namespace)
                {
                    owner[index] = candidate;
                    break;
                }
            }
        }

        // One node class per control, whichever manifest declared it: a control registered twice would
        // otherwise become two classes the reconciler can never patch between.
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var claims = new Dictionary<(string Namespace, string NodeName), List<(int Manifest, ControlModel Control)>>();
        var order = new List<(string Namespace, string NodeName)>();

        for (int index = 0; index < manifests.Count; index++)
        {
            if (manifests[index].Sweeps)
            {
                continue;
            }

            foreach (var control in manifests[index].Controls)
            {
                if (seen.ContainsKey(control.ControlType))
                {
                    diagnostics.Add(Info(Diagnostics.DuplicateRegistration, control.Location, control.ControlType));
                    continue;
                }

                seen.Add(control.ControlType, index);
                Claim(manifests[index], index, control);
            }
        }

        // A control the application named itself is left to that registration, including the error two
        // registrations of it would give it. That is what lets [VirtualDomAll] be added to a manifest
        // that already lists controls without turning every one of them into a duplicate.
        var explicitTypes = new HashSet<string>(seen.Keys, StringComparer.Ordinal);

        for (int index = 0; index < manifests.Count; index++)
        {
            if (!manifests[index].Sweeps)
            {
                continue;
            }

            foreach (var swept in controls.Controls)
            {
                var type = swept.ToDisplayString(Formats.Type);

                // Two sweeps in one compilation would otherwise claim every control twice; the first
                // one to claim a control keeps it, silently, because a sweep names no controls and so
                // has no names for a duplicate to collide with.
                if (explicitTypes.Contains(type) || seen.ContainsKey(type))
                {
                    continue;
                }

                seen.Add(type, owner[index]);
                Claim(manifests[index], owner[index], Model(swept, swept.Name, manifests[index].Location, diagnostics));
            }
        }

        void Claim(ManifestModel model, int index, ControlModel control)
        {
            var key = (model.Namespace ?? string.Empty, control.NodeName);

            if (!claims.TryGetValue(key, out var list))
            {
                list = [];
                claims.Add(key, list);
                order.Add(key);
            }

            list.Add((index, control));
        }

        var perManifestControls = new List<ControlModel>[manifests.Count];
        var perManifestPlaceholders = new List<TypePlaceholderModel>[manifests.Count];

        for (int index = 0; index < manifests.Count; index++)
        {
            perManifestControls[index] = [];
            perManifestPlaceholders[index] = [];
        }

        foreach (var key in order)
        {
            var list = claims[key];

            if (list.Count == 1)
            {
                var single = list[0];
                perManifestControls[single.Manifest].Add(Documentate(
                    Resolve(single.Control, catalog, manifests[single.Manifest].Overrides, diagnostics),
                    documentation));

                continue;
            }

            // Two controls, one name: neither node class is emitted, and the name becomes a placeholder
            // that reports its own use rather than producing one of the two and quietly dropping the
            // other.
            perManifestPlaceholders[list[0].Manifest].Add(new TypePlaceholderModel(
                list[0].Control.NodeName,
                list[0].Control.Accessibility,
                [.. list.Select(entry => entry.Control.ControlMetadataName)]));
        }

        foreach (var diagnostic in diagnostics)
        {
            context.ReportDiagnostic(diagnostic.ToDiagnostic());
        }

        for (int index = 0; index < manifests.Count; index++)
        {
            if (perManifestControls[index].Count == 0 && perManifestPlaceholders[index].Count == 0)
            {
                continue;
            }

            var model = manifests[index];
            var source = new StringBuilder();
            NodeEmitter.Write(
                model with
                {
                    Controls = [.. perManifestControls[index]],
                    Placeholders = [.. perManifestPlaceholders[index]],
                },
                source);

            var hint = (model.Namespace is null ? "Global" : model.Namespace.Replace('.', '_'))
                + "." + model.Name + ".VirtualDom.g.cs";
            context.AddSource(hint, source.ToString());
        }
    }

    /// <summary>One name on one node class, and what claimed it.</summary>
    private sealed class Claim
    {
        private Claim(string name, PropertyModel? native, ExtensionMember? member)
        {
            Name = name;
            Native = native;
            Member = member;
            Contenders = member is null ? [] : [member.Contender];
        }

        internal string Name { get; }

        internal PropertyModel? Native { get; }

        internal ExtensionMember? Member { get; private set; }

        internal bool IsNative => Native is not null;

        /// <summary>Every claim on the name; more than one means the name is in dispute.</summary>
        internal List<string> Contenders { get; }

        /// <summary>How many claims there are, which is what decides whether the name is a placeholder.</summary>
        internal int Holds => Contenders.Count;

        internal static Claim ForNative(PropertyModel property) => new(property.Name, property, member: null);

        internal static Claim ForExtension(string name, ExtensionMember member) =>
            new(name, native: null, member);

        /// <summary>
        /// Adds a second claim on an extension name. The C# rules decide it where they can: a claim on a
        /// more specific receiver hides the other, exactly as a derived property hides a base one, and
        /// anything else is a dispute the analyzer reports at the use site.
        /// </summary>
        internal void Add(ExtensionMember member)
        {
            if (Contenders.Count > 1)
            {
                Contenders.Add(member.Contender);
                return;
            }

            if (Member!.Receiver != member.Receiver && Member.ReceiverChain.Contains(member.Receiver))
            {
                return;
            }

            if (Member.Receiver != member.Receiver && member.ReceiverChain.Contains(Member.Receiver))
            {
                Member = member;
                Contenders.Clear();
                Contenders.Add(member.Contender);
                return;
            }

            Contenders.Add(member.Contender);
        }

        /// <summary>The property to emit for this name, once the name is known to be undisputed.</summary>
        internal PropertyModel PropertyFor(string name) =>
            Member is null ? Native! : PropertyOf(name, Member);

        private static PropertyModel PropertyOf(string name, ExtensionMember member) =>
            new(
                name,
                member.Type,
                "_" + Lower(name),
                member.IsEvent ? null : "Pristine" + name,
                PristineLiteral: member.UnsetLiteral,
                AddHandler: member.AddHandler,
                RemoveHandler: member.RemoveHandler,
                Container: member.Container,
                Accessor: member.Name,
                Receiver: member.ReceiverType,
                Documentation: member.Documentation);
    }
}

/// <summary>One <c>[assembly: VNodeExtensionPropertyName]</c> the application declared.</summary>
/// <param name="Container">The static class, the way a reader names it.</param>
/// <param name="Member">The name the node would otherwise expose.</param>
/// <param name="Name">The name to expose instead.</param>
/// <param name="Location">Where the attribute is, to report against.</param>
internal sealed record OverrideModel(string Container, string Member, string Name, Location Location);



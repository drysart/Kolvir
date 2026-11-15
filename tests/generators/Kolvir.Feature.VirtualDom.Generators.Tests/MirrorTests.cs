using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.VirtualDom.Generators.Tests;

/// <summary>
/// The check that a node class cannot drift from the control it mirrors: every property the control
/// lets anyone set has to be settable on the node, with the same type.
/// </summary>
/// <remarks>
/// This is why the manifest is a compile-time declaration rather than a run-time call. The generator
/// reads the control with the compiler's own model, so a property added to a control appears on its
/// node the next time the project builds - and this test is what fails on the day one stops doing so.
/// A node property typed <c>object</c>, or one whose nullable annotation disagrees with the control's,
/// would compile and then fail at the assignment.
/// </remarks>
[TestClass]
public sealed class MirrorTests
{
    /// <summary>
    /// One control per kind of surface the generator has to cope with: a text control, a container, a
    /// list, one with handler pairs, and one that inherits most of its properties from an intermediate
    /// base.
    /// </summary>
    private static readonly string[] Controls =
    [
        "Label", "Button", "Panel", "StackPanel", "TextBox", "RichLabel", "CheckBox", "ComboBox",
        "ListBox", "TabControl", "ScrollBar", "SplitContainer", "Expander", "ProgressBar",
    ];

    /// <summary>Members the node base and the generated body own, so a control property cannot take them.</summary>
    private static readonly string[] Reserved = ["Key", "Children", "NodeType", "ApplyTo", "PatchFrom"];

    [TestMethod]
    public void EverySettableControlPropertyAppearsOnItsNodeWithTheSameType()
    {
        var registrations = string.Join(
            "\n",
            Controls.Select(name => "[VirtualDom(typeof(" + name + "))]"));

        var generated = VirtualDomCompiler.Build($$"""
            using Kolvir;
            using Kolvir.VirtualDom;

            namespace TestApp;

            {{registrations}}
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var missing = new List<string>();

        foreach (var name in Controls)
        {
            var control = generated.Output.GetTypeByMetadataName("Kolvir." + name);

            Assert.IsNotNull(control, name + " is not a type in this compilation");

            var node = generated.Output.GetTypeByMetadataName("TestApp.V" + name);

            Assert.IsNotNull(node, "no node class was generated for " + name);

            foreach (var property in Settable(control))
            {
                if (Reserved.Contains(property.Name))
                {
                    continue;
                }

                var onNode = node.GetMembers(property.Name).OfType<IPropertySymbol>().FirstOrDefault();

                if (onNode is null)
                {
                    missing.Add($"{name}.{property.Name} is settable on the control but not on {node.Name}");
                }
                else if (onNode.Type.ToDisplayString() != property.Type.ToDisplayString())
                {
                    missing.Add($"{name}.{property.Name} is {property.Type.ToDisplayString()} but {node.Name}.{property.Name} is {onNode.Type.ToDisplayString()}");
                }
            }
        }

        Assert.IsEmpty(missing, string.Join("\n", missing));
    }

    /// <summary>The properties a node should mirror: public, instance, settable, and not abstract.</summary>
    private static IEnumerable<IPropertySymbol> Settable(INamedTypeSymbol control)
    {
        for (var type = control; type is not null; type = type.BaseType)
        {
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (!property.IsStatic
                    && !property.IsIndexer
                    && !property.IsAbstract
                    && property.GetMethod?.DeclaredAccessibility == Accessibility.Public
                    && property.SetMethod?.DeclaredAccessibility == Accessibility.Public)
                {
                    yield return property;
                }
            }
        }
    }
}

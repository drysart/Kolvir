using Kolvir;
using Kolvir.Feature.VirtualDom.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Feature.VirtualDom.Generators.Tests;

/// <summary>
/// What a control's extension members become on its node, and how a name that two of them claim is
/// handled. The native members have <see cref="NodeEmissionTests"/> and
/// <see cref="MirrorTests"/>; everything here comes from an <c>extension(...)</c> block or a static
/// class of extension methods, which is the half the generator could not see before Roslyn 5.9.
/// </summary>
[TestClass]
public sealed class ExtensionMemberTests
{
    [TestMethod]
    public void AnExtensionPropertyBecomesASettableNodeProperty()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
            }

            public static class HostControlExtensions
            {
                extension(HostControl control)
                {
                    public int Column { get => 0; set { } }

                    public int Row { get => 0; set { } }
                }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        StringAssert.Contains(generated.Source, "public int Column");
        StringAssert.Contains(generated.Source, "public int Row");

        // Emission goes through the lowered accessors rather than C# 14 syntax, so generated code does
        // not depend on the consumer's language version or on a namespace import.
        StringAssert.Contains(generated.Source, "HostControlExtensions.set_Column(target, _column.Value);");
        StringAssert.Contains(generated.Source, "HostControlExtensions.get_Column(Pristine);");

        // The same accessor names the compiler lowers the members to are what this pins: if a Roslyn
        // update renames them, this test fails rather than the node silently setting nothing.
        Assert.IsFalse(generated.Source.Contains("target.Column", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AnExtensionHandlerPairBecomesAnEventProperty()
    {
        var generated = VirtualDomCompiler.Build("""
            using System;
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
            }

            public static class HostControlExtensions
            {
                public static IDisposable AddPingedHandler(this HostControl control, Action<object> handler) => null!;

                public static void RemovePingedHandler(this HostControl control, Action<object> handler)
                {
                }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var node = NodeClass(generated, "VHostControl");

        StringAssert.Contains(node, "public global::System.Action<object>? Pinged");

        // The value lives in the node's extension store, and assigning it is what installs the pair.
        StringAssert.Contains(node, "global::Kolvir.Feature.VirtualDom.VNodeExtensionActuatorSet.Get<global::System.Action<object>?>(this,");
        StringAssert.Contains(node, "global::Kolvir.Feature.VirtualDom.VNodeExtensionActuatorSet.Set(this,");

        // The point of the shape: the class names its one apply entry point, not the pair's accessors.
        StringAssert.Contains(node, "ApplyExtensionActuators(null, target);");
        StringAssert.Contains(node, "ApplyExtensionActuators(old, target);");
        Assert.IsFalse(node.Contains("AddPingedHandler", StringComparison.Ordinal), node);
        Assert.IsFalse(node.Contains("RemovePingedHandler", StringComparison.Ordinal), node);

        // The accessors are named once, in the actuator the setter installs.
        StringAssert.Contains(generated.Source, "HostControlExtensions.AddPingedHandler(");
        StringAssert.Contains(generated.Source, "HostControlExtensions.RemovePingedHandler(");
    }

    [TestMethod]
    public void OneHandlerPairIsInstalledOnceForEveryNodeClassItAppliesTo()
    {
        // A Control receiver applies to every control, so both node classes expose the pair. One token
        // and one actuator, because the value is stored under the member's identity rather than under
        // the class's: two would mean applying a single handler twice.
        var generated = VirtualDomCompiler.Build("""
            using System;
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public static class ControlExtensions
            {
                public static IDisposable AddPingedHandler(this Kolvir.Control control, Action<object> handler) => null!;

                public static void RemovePingedHandler(this Kolvir.Control control, Action<object> handler)
                {
                }
            }

            [VirtualDom(typeof(Label))]
            [VirtualDom(typeof(Panel))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        Assert.AreEqual(2, Occurrences(generated.Source, "public global::System.Action<object>? Pinged"), generated.Source);
        Assert.AreEqual(1, Occurrences(generated.Source, "internal static readonly object Token_TestApp_ControlExtensions_Pinged_"), generated.Source);
        Assert.AreEqual(1, Occurrences(generated.Source, "internal static void Apply_TestApp_ControlExtensions_Pinged_"), generated.Source);
        StringAssert.Contains(generated.Source, "internal static class App_VNodeExtensionActuators");
    }

    [TestMethod]
    public void AnExtensionOnABaseReachesEveryControlBelowIt()
    {
        var generated = VirtualDomCompiler.Build("""
            using System;
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public static class TextBoxExtensions
            {
                extension(TextBox box)
                {
                    public bool Wrapped { get => false; set { } }
                }

                public static void AddSpunHandler(this TextBox box, Action handler)
                {
                }

                public static void RemoveSpunHandler(this TextBox box, Action handler)
                {
                }
            }

            [VirtualDom(typeof(TextBox))]
            [VirtualDom(typeof(Label))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var textBox = NodeClass(generated, "VTextBox");
        var label = NodeClass(generated, "VLabel");

        StringAssert.Contains(textBox, "public bool Wrapped");
        StringAssert.Contains(textBox, "public global::System.Action? Spun");

        // A Label is not assignable to a TextBox receiver, so nothing from that block applies.
        Assert.IsFalse(label.Contains("Wrapped", StringComparison.Ordinal));
        Assert.IsFalse(label.Contains("Spun", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AGetOnlyExtensionPropertyIsSkipped()
    {
        // A node property that cannot be assigned is worse than no property at all: the initializer a
        // caller writes would be the thing that failed. EffectiveInView is the real example.
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
            }

            public static class HostControlExtensions
            {
                extension(HostControl control)
                {
                    public int Spotlight => 1;
                }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsFalse(generated.Source.Contains("Spotlight", StringComparison.Ordinal), generated.Source);
    }

    [TestMethod]
    public void AnExtensionMemberYieldsToTheControlsOwnProperty()
    {
        // C# resolves an instance member before it looks at an extension, so the extension could never
        // be reached. That is the same silence a hidden base property gets, not a conflict.
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
                public int Column { get => 0; set { } }
            }

            public static class HostControlExtensions
            {
                extension(HostControl control)
                {
                    public int Column { get => 0; set { } }
                }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        Assert.AreEqual(1, Occurrences(generated.Source, "public int Column"));
        Assert.IsFalse(generated.Source.Contains("HostControlExtensions.set_Column", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AnOverrideRenamesAnExtensionMemberInFull()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            [assembly: VNodeExtensionPropertyName(typeof(TestApp.HostControlExtensions), "Column", "GridColumn")]

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
            }

            public static class HostControlExtensions
            {
                extension(HostControl control)
                {
                    public int Column { get => 0; set { } }
                }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        StringAssert.Contains(generated.Source, "public int GridColumn");
        Assert.IsFalse(generated.Source.Contains("public int Column", StringComparison.Ordinal));
        StringAssert.Contains(generated.Source, "HostControlExtensions.set_Column(target,");
    }

    [TestMethod]
    public void AnOverrideOntoANativeNameIsReportedAsVD015()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            [assembly: VNodeExtensionPropertyName(typeof(TestApp.HostControlExtensions), "Column", "Title")]

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
                public string Title { get; set; } = "pristine";
            }

            public static class HostControlExtensions
            {
                extension(HostControl control)
                {
                    public int Column { get => 0; set { } }
                }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        CollectionAssert.Contains(generated.Ids, "VD015");
        Assert.AreEqual(1, Occurrences(generated.Source, "public string Title"));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));
    }

    [TestMethod]
    public void TwoMembersClaimingOneNameEmitOneInertPlaceholder()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
            }

            public static class FirstExtensions
            {
                extension(HostControl control)
                {
                    public int Column { get => 0; set { } }
                }
            }

            public static class SecondExtensions
            {
                extension(HostControl control)
                {
                    public int Column { get => 0; set { } }
                }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        // The conflict is not a generator error: it is reported where the name is used, which is what
        // makes an unreferenced placeholder harmless.
        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var source = generated.Source;

        StringAssert.Contains(source, "public object? Column { get; set; }");
        StringAssert.Contains(source, "[global::Kolvir.Feature.VirtualDom.VNodeNameConflict(\"Column\", \"TestApp.FirstExtensions::Column\", \"TestApp.SecondExtensions::Column\")]");

        Assert.IsFalse(source.Contains("public int Column", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Obsolete", StringComparison.Ordinal));

        // Nothing generated may name the placeholder: a generated reference would make the whole
        // application unbuildable instead of the conflicting name.
        Assert.IsFalse(source.Contains("set_Column", StringComparison.Ordinal), source);
        Assert.IsFalse(source.Contains("get_Column", StringComparison.Ordinal), source);
        Assert.IsFalse(source.Contains("_column", StringComparison.Ordinal), source);
    }

    [TestMethod]
    public void AnExtensionOnAMoreSpecificReceiverHidesOneOnABase()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public static class ControlExtensions
            {
                extension(Kolvir.Control control)
                {
                    public int Slot { get => 0; set { } }
                }
            }

            public static class TextBoxExtensions
            {
                extension(TextBox box)
                {
                    public int Slot { get => 0; set { } }
                }
            }

            [VirtualDom(typeof(TextBox))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        Assert.AreEqual(1, Occurrences(generated.Source, "public int Slot"));
        StringAssert.Contains(generated.Source, "TextBoxExtensions.set_Slot(target,");
        Assert.IsFalse(generated.Source.Contains("ControlExtensions.set_Slot", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TwoControlsWantingOneNodeNameProduceOneInertPlaceholder()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace Acme
            {
                public sealed class Button : Kolvir.Control;
            }

            namespace Widgets
            {
                public sealed class Button : Kolvir.Control;
            }

            namespace TestApp
            {
                [VirtualDom(typeof(Acme.Button))]
                [VirtualDom(typeof(Widgets.Button))]
                internal partial class App;
            }
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));

        var source = generated.Source;

        StringAssert.Contains(source, "class VButton;");
        StringAssert.Contains(source, "VNodeNameConflict(\"VButton\", \"Acme.Button\", \"Widgets.Button\")");

        // Neither real node was emitted: a choice between the two would be the generator's rather than
        // the application's.
        Assert.IsFalse(source.Contains("class VButton : ", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("NodeType", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AnUnsetValueBecomesAConstantRatherThanAConstructedRead()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
                [VNodePropertyUnsetValue(7)]
                public int Column { get => 0; set { } }

                [VNodePropertyUnsetValue(null)]
                public string? Title { get; set; }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        StringAssert.Contains(generated.Source, "private static readonly int PristineColumn = 7;");
        StringAssert.Contains(generated.Source, "private static readonly string? PristineTitle = null;");

        // The declared value replaces the read: nothing asks the constructed control for either, which
        // is the point of pinning a default the control's constructor was never the authority on.
        Assert.IsFalse(generated.Source.Contains("Pristine.Column", StringComparison.Ordinal), generated.Source);
        Assert.IsFalse(generated.Source.Contains("Pristine.Title", StringComparison.Ordinal), generated.Source);
    }

    [TestMethod]
    public void TheControlsOwnConstructorStillAnswersWithoutADeclaredValue()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
                [VNodePropertyUnsetValue(7)]
                public int Column { get => 0; set { } }

                public string Title { get; set; } = "pristine";
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));

        StringAssert.Contains(generated.Source, "private static readonly int PristineColumn = 7;");
        StringAssert.Contains(generated.Source, "private static readonly string PristineTitle = Pristine.Title;");

        // One shared control answers for the property that has to be asked, and only one: two
        // constructions is the create delegate plus that instance.
        Assert.AreEqual(2, Occurrences(generated.Source, "new global::TestApp.HostControl()"));
    }

    [TestMethod]
    public void ATableLayoutPanelNodeExposesItsExtensionsFromTheRealAssembly()
    {
        // The end-to-end shape: the library arrives as metadata, which is the case Roslyn 4.14 could
        // not read, and the four table properties and the popup pair are what it contributes.
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(TableLayoutPanel))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var source = generated.Source;

        foreach (var name in new[] { "TableColumn", "TableRow", "TableColumnSpan", "TableRowSpan" })
        {
            StringAssert.Contains(source, "public int " + name);
            StringAssert.Contains(source, "global::Kolvir.TableLayoutPanelExtensions.set_" + name + "(target,");
        }

        StringAssert.Contains(source, "public global::System.Action<object, global::Kolvir.PopupResult>? PopupClosed");
        StringAssert.Contains(source, "global::Kolvir.PopupExtensions.AddPopupClosedHandler(");
    }

    [TestMethod]
    public void TheBlockShapeTheGeneratorReadsIsWhatRoslynReports()
    {
        // Pins the symbol shape the feature is built on: if a Roslyn update stops reporting a block as
        // an extension type with an ExtensionParameter, this fails rather than every node quietly
        // losing its extension properties.
        var generated = VirtualDomCompiler.Build("""
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            internal partial class App;
            """);

        var container = generated.Output.GetTypeByMetadataName("Kolvir.TableLayoutPanelExtensions");

        Assert.IsNotNull(container, "the library is not in this compilation");

        var block = container.GetMembers().OfType<INamedTypeSymbol>().SingleOrDefault(type => type.IsExtension);

        Assert.IsNotNull(block, "the extension block is no longer a member of the static class");
        Assert.AreEqual("Kolvir.Control", block.ExtensionParameter?.Type.ToDisplayString());

        var column = block.GetMembers().OfType<IPropertySymbol>().Single(property => property.Name == "TableColumn");

        Assert.IsNotNull(column.SetMethod);
        Assert.AreEqual(Accessibility.Public, column.SetMethod.DeclaredAccessibility);
    }

    [TestMethod]
    public void AnAssemblyThatDoesNotReferenceKolvirIsNotScanned()
    {
        // The scan is narrowed to assemblies that can name the receiver. An assembly that extends
        // `object` from outside that set is the documented hole, and this is what it looks like.
        var library = VirtualDomCompiler.Library("ObjectExtensions", """
            public static class ObjectExtensions
            {
                extension(object target)
                {
                    public int Hidden { get => 0; set { } }
                }
            }
            """, referencesKolvir: false);

        var generated = VirtualDomCompiler.Build([library], """
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(Label))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsFalse(generated.Source.Contains("Hidden", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AnAssemblyThatReferencesKolvirIsScanned()
    {
        var library = VirtualDomCompiler.Library("ControlExtensions", """
            public static class ControlExtensions
            {
                extension(Kolvir.Control target)
                {
                    public int Shiny { get => 0; set { } }
                }
            }
            """, referencesKolvir: true);

        var generated = VirtualDomCompiler.Build([library], """
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(Label))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));
        StringAssert.Contains(generated.Source, "public int Shiny");
        StringAssert.Contains(generated.Source, "global::ControlExtensions.set_Shiny(target,");
    }

    /// <summary>One generated node class and everything the emitter wrote for it, by class name.</summary>
    private static string NodeClass(Generated generated, string name)
    {
        const string Marker = "/// <summary>A node that makes a ";
        const string NodeClassEnd = "/// <summary>The extension handler pairs this file's node classes install on first use.</summary>";

        foreach (var block in generated.Source.Split(Marker).Skip(1))
        {
            if (block.Contains("class " + name + " ", StringComparison.Ordinal))
            {
                // The actuator holder is file-wide rather than per class, so it is cut off here -
                // otherwise the last node class's block would include it.
                return block.Split(NodeClassEnd)[0];
            }
        }

        Assert.Fail("no class named " + name + " was generated");

        return string.Empty;
    }

    private static int Occurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;
}




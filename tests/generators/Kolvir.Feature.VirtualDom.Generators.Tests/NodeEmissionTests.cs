using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Feature.VirtualDom.Generators.Tests;

/// <summary>What a manifest becomes: the node classes, their members, and the fact that they compile.</summary>
[TestClass]
public sealed class NodeEmissionTests
{
    [TestMethod]
    public void OneNodeClassPerRegistrationInOneFile()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(Label))]
            [VirtualDom(typeof(Button))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids);
        Assert.HasCount(1, generated.Sources);

        var source = generated.Source;
        StringAssert.Contains(source, "public sealed class VLabel : global::Kolvir.Feature.VirtualDom.VNode");
        StringAssert.Contains(source, "public sealed class VButton : global::Kolvir.Feature.VirtualDom.VNode");
        StringAssert.Contains(source, "namespace TestApp");
    }

    [TestMethod]
    public void GeneratedCodeCompiles()
    {
        // The important one: every member the emitter writes has to be valid against the control it
        // was written for, including the inherited ones and the nullable annotations.
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(Label))]
            [VirtualDom(typeof(Button))]
            [VirtualDom(typeof(Panel))]
            [VirtualDom(typeof(StackPanel))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids);

        // A control whose property has no public setter must not be flattened, so this is also the
        // assertion that the property filter held.
        var errors = generated.CompileErrors;
        Assert.IsEmpty(errors);
    }

    [TestMethod]
    public void NodeCarriesTheControlsOwnPropertyType()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(Label))]
            internal partial class App;

            public sealed class HostControl : Kolvir.Control
            {
                public string Title { get; set; } = "pristine";
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class More;
            """);

        Assert.IsEmpty(generated.Ids);

        // The nullable annotation travels with the type: a property that can be null is a property
        // whose node can be set to null, and the two must agree or the analyzer's premise is wrong.
        StringAssert.Contains(generated.Source, "public string Title");
        StringAssert.Contains(generated.Source, "PristineTitle = Pristine.Title;");
    }

    [TestMethod]
    public void LeafControlsAreMarkedAndContainersAreNot()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(Label))]
            [VirtualDom(typeof(Panel))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids);

        var label = NodeClass(generated, "VLabel");
        var panel = NodeClass(generated, "VPanel");

        StringAssert.Contains(label, "[global::Kolvir.Feature.VirtualDom.LeafNode]");
        Assert.IsFalse(panel.Contains("[global::Kolvir.Feature.VirtualDom.LeafNode]", StringComparison.Ordinal));
    }

    [TestMethod]
    public void HandlerPairBecomesAnEventProperty()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
                public void AddClickedHandler(System.Action handler) { }

                public void RemoveClickedHandler(System.Action handler) { }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids);

        var source = generated.Source;
        StringAssert.Contains(source, "public global::System.Action? Clicked");
        StringAssert.Contains(source, "target.AddClickedHandler(");
        StringAssert.Contains(source, "target.RemoveClickedHandler(");
        Assert.IsEmpty(generated.CompileErrors);
    }

    [TestMethod]
    public void AccessibilityFollowsTheControl()
    {
        // A node for an internal control cannot be public: it would expose a type the assembly does
        // not, and the generated file would not compile.
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            internal sealed class InnerControl : Kolvir.Control { }

            [VirtualDom(typeof(Label))]
            [VirtualDom(typeof(InnerControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids);

        StringAssert.Contains(generated.Source, "public sealed class VLabel");
        StringAssert.Contains(generated.Source, "internal sealed class VInnerControl");
        Assert.IsEmpty(generated.CompileErrors);
    }

    [TestMethod]
    public void PristineValuesComeFromOneControlPerNodeType()
    {
        // A node type restores a dropped property to what its control's constructor left, and it
        // captures those values once rather than constructing a control per property at type load.
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(Label))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids);

        var source = generated.Source;
        var constructions = System.Text.RegularExpressions.Regex.Matches(source, @"new global::Kolvir\.Label\(\)");

        // Two: the node type's Create delegate, and the one instance the pristine values are read from.
        Assert.AreEqual(2, constructions.Count);
    }

    [TestMethod]
    public void EmitsNothingForAManifestWithNoControls()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids);
        Assert.IsEmpty(generated.Sources);
    }

    /// <summary>One generated node class and everything the emitter wrote above it, by class name.</summary>
    private static string NodeClass(Generated generated, string name)
    {
        const string Marker = "/// <summary>A node that makes a ";

        foreach (var block in generated.Source.Split(Marker).Skip(1))
        {
            if (block.Contains("class " + name + " ", StringComparison.Ordinal))
            {
                return block;
            }
        }

        Assert.Fail("no class named " + name + " was generated");

        return string.Empty;
    }
}

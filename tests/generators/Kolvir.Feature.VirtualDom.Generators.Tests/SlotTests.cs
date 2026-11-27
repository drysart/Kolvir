using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Feature.VirtualDom.Generators.Tests;

/// <summary>
/// Property slots at the emit step: which properties become slots, what a slot looks like on the node,
/// and the fact that a node for the wrong control is a compile error rather than a run-time surprise.
/// </summary>
[TestClass]
public sealed class SlotTests
{
    private const string Host = """
        using Kolvir;
        using Kolvir.Feature.VirtualDom;

        namespace TestApp;

        /// <summary>A control with one control-typed property of each kind.</summary>
        public sealed class HostControl : Kolvir.Control
        {
            /// <summary>The control shown in the header.</summary>
            public Panel Header { get; } = new();

            /// <summary>A body a node replaces.</summary>
            public Panel? Body { get; set; }
        }
        """;

    [TestMethod]
    public void ASlotIsEmittedWithTheCovariantMarkerAndTheControlsDocumentation()
    {
        var generated = VirtualDomCompiler.Build(Host + """

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var source = generated.Source;

        StringAssert.Contains(
            source,
            "public global::Kolvir.Feature.VirtualDom.IVNodeFor<global::Kolvir.Panel>? Header",
            "the slot property takes a node for a Panel, not a Panel");

        StringAssert.Contains(
            source,
            "/// <summary>The control shown in the header.</summary>",
            "and carries the documentation the control's property has");

        StringAssert.Contains(source, "global::Kolvir.Feature.VirtualDom.VNodeSlot.Borrowed(");
    }

    [TestMethod]
    public void EveryNodeCarriesOneMarkerForItsOwnControlType()
    {
        var generated = VirtualDomCompiler.Build(Host + """

            [VirtualDom(typeof(HostControl))]
            [VirtualDom(typeof(Label))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var source = generated.Source;

        StringAssert.Contains(
            source,
            "public sealed class VLabel : global::Kolvir.Feature.VirtualDom.VNode, global::Kolvir.Feature.VirtualDom.IVNodeFor<global::Kolvir.Label>");

        StringAssert.Contains(
            source,
            "global::Kolvir.Feature.VirtualDom.VNode global::Kolvir.Feature.VirtualDom.IVNodeFor<global::Kolvir.Label>.Node => this;",
            "one explicit member, and no instantiation per ancestor");
    }

    [TestMethod]
    public void APropertyWithNoSetterIsBorrowedAndOneWithASetterCreates()
    {
        var generated = VirtualDomCompiler.Build(Host + """

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var source = generated.Source;

        StringAssert.Contains(
            source,
            "VNodeSlot.Borrowed(target.Header, _header.Value?.Node)",
            "a get-only property hands the node the control it already holds");

        StringAssert.Contains(
            source,
            "VNodeSlot.Creating(_body.Value?.Node, static (owner, control) => ((global::TestApp.HostControl)owner).Body = (global::Kolvir.Panel)control)",
            "a settable one has the reconciler make the control and assign it");
    }

    [TestMethod]
    public void ANodeForAControlThatIsNotTheSlotsTypeDoesNotCompile()
    {
        var generated = VirtualDomCompiler.Build(Host + """

            [VirtualDom(typeof(HostControl))]
            [VirtualDom(typeof(Label))]
            internal partial class App;

            internal static class Use
            {
                // A Label node in a Panel slot: IVNodeFor<Label> has no conversion to IVNodeFor<Panel>.
                internal static VHostControl Wrong() => new VHostControl { Header = new VLabel() };
            }
            """);

        Assert.IsNotEmpty(generated.CompileErrors, "assigning the wrong node should not compile");

        var error = generated.CompileErrors.First(entry => entry.Contains("CS0029", StringComparison.Ordinal) || entry.Contains("CS0266", StringComparison.Ordinal));

        StringAssert.Contains(error, "VLabel");
        StringAssert.Contains(error, "IVNodeFor<Kolvir.Panel>");
    }

    [TestMethod]
    public void SlotOrderFollowsThePropertyModel()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class TwoSlots : Kolvir.Control
            {
                public Panel First { get; } = new();

                public Panel Second { get; } = new();
            }

            [VirtualDom(typeof(TwoSlots))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var source = generated.Source;

        int firstProperty = source.IndexOf("? First", StringComparison.Ordinal);
        int secondProperty = source.IndexOf("? Second", StringComparison.Ordinal);
        int firstSlot = source.IndexOf("Borrowed(target.First", StringComparison.Ordinal);
        int secondSlot = source.IndexOf("Borrowed(target.Second", StringComparison.Ordinal);

        Assert.IsTrue(firstProperty >= 0 && secondProperty > firstProperty, "the properties are emitted in model order");
        Assert.IsTrue(firstSlot >= 0 && secondSlot > firstSlot, "and so are the slots, which is what pairs slot n with slot n");
    }

    [TestMethod]
    public void AControlsParentIsNotASlot()
    {
        // Control.Parent is control-typed and get-only from outside, but its setter is the Control's own
        // - the parent's Controls collection writes it - so it is state, not a place for a node.
        var generated = VirtualDomCompiler.Build(Host + """

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        Assert.IsFalse(
            generated.Source.Contains("? Parent", StringComparison.Ordinal),
            "Control.Parent should not have become a node property");
    }

    [TestMethod]
    public void AControlReturnedByItsOwnKindIsNotASlot()
    {
        // MenuView.RootMenu returns the outermost view of its chain - this view when there is no parent.
        // A node there would be a second node on a control that already has one, so a property of the
        // declaring control's own kind is a reference rather than a place for contents.
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public class Chain : Kolvir.Control
            {
                public Chain Root => this;

                public Panel Body { get; } = new();
            }

            [VirtualDom(typeof(Chain))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var node = generated.Output.GetTypeByMetadataName("TestApp.VChain");

        Assert.IsNotNull(node);
        Assert.IsEmpty(node.GetMembers("Root"), "a reference to a control of the same kind is not a slot");
        Assert.IsNotEmpty(node.GetMembers("Body"), "a control of another kind still is");
    }

    [TestMethod]
    public void ALazilyCreatedControlIsNotASlot()
    {
        // Panel.HorizontalScrollBar is a ScrollBar? the scrolling feature creates and drops; there is
        // nothing to fill at mount time, so it is not a slot.
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(Panel))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var node = generated.Output.GetTypeByMetadataName("TestApp.VPanel");

        Assert.IsNotNull(node);
        Assert.IsEmpty(node.GetMembers("HorizontalScrollBar"), "a nullable, lazily created control is not a slot");
        Assert.IsEmpty(node.GetMembers("VerticalScrollBar"));
    }

    [TestMethod]
    public void OnlyAControlASlotCanHoldCarriesTheAllUnassignedInstance()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(SplitContainer))]
            [VirtualDom(typeof(Panel))]
            [VirtualDom(typeof(TableLayoutPanel))]
            [VirtualDom(typeof(Label))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var output = generated.Output;

        foreach (var name in new[] { "VPanel", "VTableLayoutPanel" })
        {
            var node = output.GetTypeByMetadataName("TestApp." + name);

            Assert.IsNotNull(node);
            Assert.IsNotNull(
                node.GetMembers("UnassignedNode").FirstOrDefault(),
                name + " can be given to a Panel slot, so a removal restores through it");
        }

        var label = output.GetTypeByMetadataName("TestApp.VLabel");

        Assert.IsNotNull(label);
        Assert.IsEmpty(label.GetMembers("UnassignedNode"), "a Label node can never be in a slot here, so it does not pay for one");
    }
}

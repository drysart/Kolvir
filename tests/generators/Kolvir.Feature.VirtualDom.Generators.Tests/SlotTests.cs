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
    public void ASlotIsEmittedWithTheNodeClassForItsControlsTypeAndTheControlsDocumentation()
    {
        var generated = VirtualDomCompiler.Build(Host + """

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var source = generated.Source;

        StringAssert.Contains(
            source,
            "public global::TestApp.VPanel? Header",
            "a borrowed slot takes the node class for the control it holds, not a node for anything derived from it");

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
            "VNodeSlot.Borrowed(target.Header, _header.Value)",
            "a get-only property hands the node the control it already holds, with no marker to read through");

        StringAssert.Contains(
            source,
            "VNodeSlot.Creating(_body.Value?.Node, static (owner, control) => ((global::TestApp.HostControl)owner).Body = (global::Kolvir.Panel)control)",
            "a settable one has the reconciler make the control and assign it");
    }

    [TestMethod]
    public void ANodeForAControlThatIsNotTheCreatingSlotsTypeDoesNotCompile()
    {
        var generated = VirtualDomCompiler.Build(Host + """

            [VirtualDom(typeof(HostControl))]
            [VirtualDom(typeof(Label))]
            internal partial class App;

            internal static class Use
            {
                // A Label node in a Panel slot: IVNodeFor<Label> has no conversion to IVNodeFor<Panel>.
                internal static VHostControl Wrong() => new VHostControl { Body = new VLabel() };
            }
            """);

        Assert.IsNotEmpty(generated.CompileErrors, "assigning the wrong node should not compile");

        var error = generated.CompileErrors.First(entry => entry.Contains("CS0029", StringComparison.Ordinal) || entry.Contains("CS0266", StringComparison.Ordinal));

        StringAssert.Contains(error, "VLabel");
        StringAssert.Contains(error, "IVNodeFor<Kolvir.Panel>");
    }

    [TestMethod]
    public void ANodeForADerivedControlDoesNotCompileInABorrowedSlot()
    {
        // The panel a SplitContainer makes is a Panel, so a node for a TableLayoutPanel cannot describe
        // it: the property is typed with that one node class, and the type system rejects the rest. This
        // is the case the marker cannot cover - variance would admit it, and applying a TableLayoutPanel
        // node to a Panel is what the type is there to prevent.
        var generated = VirtualDomCompiler.Build("""

            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(SplitContainer))]
            [VirtualDom(typeof(TableLayoutPanel))]
            internal partial class App;

            internal static class Use
            {
                internal static VSplitContainer Wrong() => new VSplitContainer { Panel1 = new VTableLayoutPanel() };
            }
            """);

        Assert.IsNotEmpty(generated.CompileErrors, "a node for a derived control should not compile in a borrowed slot");

        var error = generated.CompileErrors.First(entry => entry.Contains("CS0029", StringComparison.Ordinal) || entry.Contains("CS0266", StringComparison.Ordinal));

        StringAssert.Contains(error, "VTableLayoutPanel", "the error names the node that was given");
        StringAssert.Contains(error, "VPanel", "and the node class the slot takes");
    }

    [TestMethod]
    public void ABorrowedSlotsNodeIsEmittedEvenWhenNothingElseRegisteredIt()
    {
        // Registering the container alone has to be enough: the node for its panels' type is what the
        // slot property is declared with, so the generator emits it on demand.
        var generated = VirtualDomCompiler.Build("""

            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(SplitContainer))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var output = generated.Output;
        var panel = output.GetTypeByMetadataName("TestApp.VPanel");

        Assert.IsNotNull(panel, "the Panel node the slot needs was emitted");

        var split = output.GetTypeByMetadataName("TestApp.VSplitContainer");

        Assert.IsNotNull(split);
        Assert.IsNotEmpty(split.GetMembers("Panel1"), "and the slot is typed with it");
        Assert.AreEqual(
            "TestApp.VPanel?",
            split.GetMembers("Panel1").OfType<IPropertySymbol>().Single().Type.ToDisplayString(),
            "as the node class itself, nullable because a slot may be empty");
        Assert.IsEmpty(generated.Ids, "one registration, one node, and nothing to report");
    }

    [TestMethod]
    public void ABorrowedSlotsNodeIsEmittedOnlyOnceWhenTheControlIsAlsoRegistered()
    {
        var generated = VirtualDomCompiler.Build("""

            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(SplitContainer))]
            [VirtualDom(typeof(Panel))]
            internal partial class App;

            internal static class Use
            {
                internal static VPanel Panel() => new();
                internal static VSplitContainer Split() => new();
            }
            """);

        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        Assert.IsEmpty(generated.Ids, "the implicit emission is de-duplicated, so there is no duplicate to report");

        Assert.AreEqual(
            1,
            generated.Sources.Count(entry => entry.Source.Contains("class VPanel ", StringComparison.Ordinal)),
            "one node class for Panel, however many passes wanted it");
    }

    [TestMethod]
    public void ABorrowedSlotIsAbsentWhenItsControlsTypeCannotHaveANode()
    {
        // An abstract control has no node class - there is nothing to construct - so a property typed
        // with it cannot be typed at all, and the property is left off the node rather than typed by a
        // name that does not exist.
        var generated = VirtualDomCompiler.Build("""

            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public abstract class Ghost : Kolvir.Control
            {
            }

            public sealed class Haunted : Kolvir.Control
            {
                /// <summary>Never anything, because nothing can make one.</summary>
                public Ghost Ghost => null!;

                /// <summary>A control that does have a node.</summary>
                public Panel Body { get; } = new();
            }

            [VirtualDom(typeof(Haunted))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var node = generated.Output.GetTypeByMetadataName("TestApp.VHaunted");

        Assert.IsNotNull(node);
        Assert.IsEmpty(node.GetMembers("Ghost"), "a slot whose control cannot have a node is absent, not mis-typed");
        Assert.IsNotEmpty(node.GetMembers("Body"), "and the other slot on the same control is unaffected");
        Assert.IsNull(generated.Output.GetTypeByMetadataName("TestApp.VGhost"), "no node was emitted for it either");
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
    public void OnlyAControlABorrowedSlotHoldsCarriesTheAllUnassignedInstance()
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
        var panel = output.GetTypeByMetadataName("TestApp.VPanel");

        Assert.IsNotNull(panel);
        Assert.IsNotNull(
            panel.GetMembers("UnassignedNode").FirstOrDefault(),
            "VPanel is what a removal restores the container's panels from");

        foreach (var name in new[] { "VTableLayoutPanel", "VLabel" })
        {
            var node = output.GetTypeByMetadataName("TestApp." + name);

            Assert.IsNotNull(node);
            Assert.IsEmpty(
                node.GetMembers("UnassignedNode"),
                name + " is never a borrowed slot here, so it does not pay for a restore instance");
        }
    }
}

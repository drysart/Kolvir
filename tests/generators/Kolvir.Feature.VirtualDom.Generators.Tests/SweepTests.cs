using Kolvir;
using Kolvir.Feature.VirtualDom.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Feature.VirtualDom.Generators.Tests;

/// <summary>
/// What <c>[VirtualDomAll]</c> sweeps, what it leaves alone, and what it does when two controls want
/// one node class name. Registration and the sweep share everything after the control is chosen, so
/// what is asserted here is the choice rather than the emission.
/// </summary>
[TestClass]
public sealed class SweepTests
{
    [TestMethod]
    public void ASweepEmitsANodeForEveryControlItCanBuild()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp
            {
                public sealed class Alpha : Kolvir.Control
                {
                }

                public sealed class Beta : Kolvir.Control
                {
                }
            }

            namespace Host
            {
                [VirtualDomAll]
                internal partial class All;
            }
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        // The two controls the compilation declares, and the library's own: the sweep covers the
        // assemblies that can name Control as well as the source.
        StringAssert.Contains(generated.Source, "sealed class VAlpha");
        StringAssert.Contains(generated.Source, "sealed class VBeta");
        StringAssert.Contains(generated.Source, "sealed class VPanel");
        StringAssert.Contains(generated.Source, "sealed class VTableLayoutPanel");

        // The nodes are ordinary ones: the sweep changes which controls are chosen, not what is
        // emitted for them.
        StringAssert.Contains(generated.Source, "public override void ApplyTo(global::Kolvir.Control control)");
    }

    [TestMethod]
    public void ASweepLeavesANodeOutForEveryControlItCannotBuild()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp
            {
                public abstract class Abstract : Kolvir.Control
                {
                }

                public sealed class NeedsArguments : Kolvir.Control
                {
                    public NeedsArguments(int size) => Size = size;

                    public int Size { get; }
                }

                public sealed class Hidden : Kolvir.Control
                {
                    private Hidden()
                    {
                    }
                }

                public sealed class Generic<T> : Kolvir.Control
                {
                }
            }

            namespace Host
            {
                [VirtualDomAll]
                internal partial class All;
            }
            """);

        // Silent on purpose: there is no remedy the application could apply, so a diagnostic would be
        // a message with nothing behind it.
        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        Assert.IsFalse(generated.Source.Contains("sealed class VAbstract", StringComparison.Ordinal), "an abstract control has no node");
        Assert.IsFalse(generated.Source.Contains("sealed class VNeedsArguments", StringComparison.Ordinal), "a control with no parameterless constructor has no node");
        Assert.IsFalse(generated.Source.Contains("sealed class VHidden", StringComparison.Ordinal), "a control whose constructor is private has no node");
        Assert.IsFalse(generated.Source.Contains("sealed class VGeneric", StringComparison.Ordinal), "a generic control has no node");
    }

    [TestMethod]
    public void ARuleRegistrationWinsOverTheSweep()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp
            {
                [VirtualDom(typeof(Label), Name = "Text")]
                [VirtualDomAll]
                internal partial class All;
            }
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        // The named one keeps the name it was given, and is not emitted twice: two node classes for
        // one control is the thing the duplicate-registration error exists to prevent.
        StringAssert.Contains(generated.Source, "sealed class VText");
        Assert.IsFalse(generated.Source.Contains("sealed class VLabel", StringComparison.Ordinal), "the swept copy of a registered control must not be emitted");
        Assert.AreEqual(
            1,
            CountOf(generated.Source, "typeof(global::Kolvir.Label)"),
            "one control is one node class, however it was registered");
    }

    [TestMethod]
    public void ADuplicateRegistrationOfOneControlStillErrorsBesideASweep()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp
            {
                [VirtualDom(typeof(Label))]
                [VirtualDom(typeof(Label), Name = "Other")]
                [VirtualDomAll]
                internal partial class All;
            }
            """);

        CollectionAssert.Contains(generated.Ids, "VD004");
    }

    [TestMethod]
    public void ASweepEmitsAPlaceholderRatherThanTwoNodeClassesForOneName()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace Acme
            {
                public sealed class Button : Kolvir.Control
                {
                }
            }

            namespace Widgets
            {
                public sealed class Button : Kolvir.Control
                {
                }
            }

            namespace Host
            {
                [VirtualDomAll]
                internal partial class All;
            }
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        // Neither real node, one placeholder carrying the contenders, and the library's own Button
        // dragged in with them because all three want VButton.
        Assert.IsFalse(generated.Source.Contains("sealed class VButton : global::Kolvir.Feature.VirtualDom.VNode", StringComparison.Ordinal), "a disputed name must not pick a winner");
        StringAssert.Contains(generated.Source, "sealed class VButton;");
        StringAssert.Contains(generated.Source, "Acme.Button");
        StringAssert.Contains(generated.Source, "Widgets.Button");
    }

    [TestMethod]
    public void ASweepPutsTheExtensionMembersOnEveryNode()
    {
        var library = VirtualDomCompiler.Library(
            "Gallery",
            """
            using Kolvir;

            public static class GalleryExtensions
            {
                extension(Control control)
                {
                    public int Slot { get => 0; set { } }
                }
            }
            """,
            referencesKolvir: true);

        var generated = VirtualDomCompiler.Build([library], """
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace Host
            {
                [VirtualDomAll]
                internal partial class All;
            }
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        // One declaration per node class, so the count is the number of nodes: the sweep does not
        // quietly emit a node without the members a registered one would have.
        Assert.AreEqual(
            CountOf(generated.Source, "public override void ApplyTo(global::Kolvir.Control control)"),
            CountOf(generated.Source, "public int Slot"),
            "every swept node should expose the extension property");

        StringAssert.Contains(generated.Source, "global::GalleryExtensions.set_Slot(target, ");
    }

    [TestMethod]
    public void ASweepDoesNotWalkAnAssemblyThatCannotNameControl()
    {
        // A library that cannot see Kolvir cannot declare a control, and the sweep is narrowed to the
        // assemblies that can. A type of the same simple name there must not reach the claims.
        var library = VirtualDomCompiler.Library(
            "Lookalike",
            """
            namespace Widgets
            {
                public sealed class Button
                {
                    public int Size { get; set; }
                }
            }
            """,
            referencesKolvir: false);

        var generated = VirtualDomCompiler.Build([library], """
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace Host
            {
                [VirtualDomAll]
                internal partial class All;
            }
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        // Kolvir's Button still owns the name: nothing from the unscanned assembly claimed it.
        StringAssert.Contains(generated.Source, "sealed class VButton : global::Kolvir.Feature.VirtualDom.VNode");
        Assert.IsFalse(generated.Source.Contains("Widgets.Button", StringComparison.Ordinal), "an assembly that cannot name Control is not swept");
    }

    [TestMethod]
    public void ASweepEmitsNoRegistryOfNodeTypes()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp
            {
                public sealed class Alpha : Kolvir.Control
                {
                }
            }

            namespace Host
            {
                [VirtualDomAll]
                internal partial class All;
            }
            """);

        // A table of node types would root every generated class, which is exactly what makes a node
        // type an application never constructs trimmable. Nothing here may create one.
        Assert.IsFalse(generated.Source.Contains("NodeType[]", StringComparison.Ordinal), "a sweep must not emit a registry");
        Assert.IsFalse(generated.Source.Contains("typeof(V", StringComparison.Ordinal), "a node type must not be named by anything but the control it makes");
    }

    [TestMethod]
    public void ASweepIntoAConflictingNameReportsTheUse()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace Acme
            {
                public sealed class Button : Kolvir.Control
                {
                }
            }

            namespace Widgets
            {
                public sealed class Button : Kolvir.Control
                {
                }
            }

            namespace Host
            {
                [VirtualDomAll]
                internal partial class All;

                internal static class Use
                {
                    internal static VNode Make() => new VButton();
                }
            }
            """);

        var diagnostics = VirtualDomCompiler.AnalyzeAsync(generated).GetAwaiter().GetResult();
        var conflicts = diagnostics.Where(diagnostic => diagnostic.Id == "VD014").ToArray();

        Assert.AreEqual(1, conflicts.Length, "using a placeholder class name should be reported once");
        StringAssert.Contains(conflicts[0].GetMessage(), "Acme.Button");
        StringAssert.Contains(conflicts[0].GetMessage(), "Widgets.Button");
    }

    private static int CountOf(string source, string fragment)
    {
        int count = 0;

        for (int index = source.IndexOf(fragment, StringComparison.Ordinal); index >= 0;
            index = source.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}

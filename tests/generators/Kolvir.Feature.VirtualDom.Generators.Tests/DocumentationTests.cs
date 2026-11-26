using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Feature.VirtualDom.Generators.Tests;

/// <summary>
/// The documentation a generated node property carries.
/// </summary>
/// <remarks>
/// Two sources, because the generator sees two kinds of control: one declared in the compilation being
/// built, whose documentation it can read from the symbol, and one from a referenced assembly, whose
/// documentation only exists in that assembly's XML file - which the compiler does not attach to the
/// reference, so the build has to hand it over as an additional file. The second case is the one that
/// decides whether the feature does anything in a real application, so it is tested with an assembly
/// built for exactly that purpose rather than with a built-in control whose file the test process
/// happens to have next to it.
/// </remarks>
[TestClass]
public sealed class DocumentationTests
{
    [TestMethod]
    public void APropertyCarriesTheSummaryItWasDocumentedWith()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
                /// <summary>The heading shown above the body.</summary>
                public string Title { get; set; } = "pristine";
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));

        StringAssert.Contains(
            generated.Source,
            "/// <summary>The heading shown above the body.</summary>");

        AssertDocumented(generated.Source, "Title");
    }

    [TestMethod]
    public void AReferencedControlsDocumentationComesFromTheAdditionalFile()
    {
        // The control arrives as metadata, which is every control a real application uses.
        var library = VirtualDomCompiler.Library("DocumentedControls", """
            namespace DocumentedControls;

            public sealed class DocumentedControl : Kolvir.Control
            {
                public string Title { get; set; } = "pristine";
            }
            """, referencesKolvir: true);

        var documentation = new InMemoryAdditionalText("DocumentedControls.xml", """
            <?xml version="1.0"?>
            <doc>
                <assembly>
                    <name>DocumentedControls</name>
                </assembly>
                <members>
                    <member name="P:DocumentedControls.DocumentedControl.Title">
                        <summary>The heading, from the referenced assembly's XML file.</summary>
                    </member>
                </members>
            </doc>
            """);

        const string Manifest = """
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(DocumentedControls.DocumentedControl))]
            internal partial class App;
            """;

        var withDocumentation = VirtualDomCompiler.Build(
            DocumentationMode.Diagnose, [documentation], [library], Manifest);

        Assert.IsEmpty(withDocumentation.Ids, string.Join("\n", withDocumentation.Ids));
        StringAssert.Contains(
            withDocumentation.Source,
            "/// <summary>The heading, from the referenced assembly's XML file.</summary>");
        AssertDocumented(withDocumentation.Source, "Title");

        // Without the additional file there is nothing to read, which is the behaviour this feature
        // exists to avoid in a real build - so it is asserted rather than assumed, and the pack target
        // that hands the file over is what makes the difference.
        var withoutDocumentation = VirtualDomCompiler.Build(
            DocumentationMode.Diagnose, [], [library], Manifest);

        Assert.IsFalse(
            withoutDocumentation.Source.Contains("The heading, from the referenced assembly", StringComparison.Ordinal));
        AssertUndocumented(withoutDocumentation.Source, "Title");
    }

    [TestMethod]
    public void LinksBecomeTextAndNothingThatDoesNotBelongOnAPropertyIsCopied()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
                /// <summary>
                /// Where the control sits in <see cref="HostControl.Title"/>, counted the way
                /// <see cref="Activity"/> is.
                /// </summary>
                /// <remarks>
                /// <para>Children are visited in order.</para>
                /// <para>Nothing here depends on the parameter <paramref name="unused"/>.</para>
                /// </remarks>
                /// <param name="unused">A parameter this property does not have.</param>
                /// <returns>Not a property's element either.</returns>
                /// <value>The whole of it.</value>
                /// <seealso cref="Activity"/>
                public int Column { get; set; }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));

        var source = generated.Source;

        // Every link is text: a documentation ID is not source cref syntax, and a cref that does not
        // resolve in the consuming compilation is a warning in somebody else's build.
        Assert.IsFalse(source.Contains("cref=", StringComparison.Ordinal), "no cref survives");
        StringAssert.Contains(source, "<c>Title</c>");
        StringAssert.Contains(source, "<c>Activity</c>");

        // Paragraph structure is kept, and the elements that belong to the member the text was
        // written for are not copied onto a property.
        StringAssert.Contains(source, "<para>Children are visited in order.</para>");
        Assert.IsFalse(source.Contains("<param ", StringComparison.Ordinal), "param is not a property element");
        Assert.IsFalse(source.Contains("<returns>", StringComparison.Ordinal), "returns is not a property element");
        Assert.IsFalse(source.Contains("seealso", StringComparison.Ordinal), "a seealso link cannot survive either");
        StringAssert.Contains(source, "<value>The whole of it.</value>");
    }

    [TestMethod]
    public void TheGeneratedFileRaisesNoDocumentationDiagnostics()
    {
        // The trap: a copied doc comment that is malformed, links to something that does not resolve,
        // or puts an element where it does not belong fails the build in the consumer, where a warning
        // can be an error. DocumentationMode.Diagnose is what turns those checks on.
        var generated = VirtualDomCompiler.Build(
            DocumentationMode.Diagnose,
            [],
            """
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
                /// <summary>One with a link to <see cref="Control.Text"/> and a keyword, <see langword="null"/>.</summary>
                public string Title { get; set; } = "pristine";

                /// <summary>One with markup: <b>bold</b> and <c>code</c> and <paramref name="nothing"/>.</summary>
                public int Count { get; set; }

                /// <summary>One with an ampersand &amp; an escaped bracket &lt;here&gt;.</summary>
                public bool Ready { get; set; }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        // The test's own source is public and undocumented, so CS1591 is expected there; the generated
        // file disables it. Everything else about a doc comment is what this asserts is clean.
        Assert.IsEmpty(generated.GeneratedWarnings, string.Join("\n", generated.GeneratedWarnings));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));
    }

    [TestMethod]
    public void AnUndocumentedPropertyCarriesNoComment()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
                /// <summary>Documented.</summary>
                public string Title { get; set; } = "pristine";

                public int Plain { get; set; }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        AssertDocumented(generated.Source, "Title");
        AssertUndocumented(generated.Source, "Plain");
    }

    [TestMethod]
    public void DocumentationWithOnlyElementsThatDoNotBelongOnAPropertyIsDropped()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
                /// <param name="value">Only a parameter.</param>
                /// <returns>Only a return value.</returns>
                public string Title
                {
                    get => _title;
                    set => _title = value;
                }

                private string _title = "pristine";
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        AssertUndocumented(generated.Source, "Title");
    }

    [TestMethod]
    public void AnExtensionMembersDocumentationReachesTheNode()
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
                    /// <summary>The column this control sits in.</summary>
                    public int Column { get => 0; set { } }
                }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        AssertDocumented(generated.Source, "Column");
        StringAssert.Contains(generated.Source, "/// <summary>The column this control sits in.</summary>");
    }

    [TestMethod]
    public void ADocumentationFileNameThatDoesNotMatchAnAssemblyIsIgnored()
    {
        var library = VirtualDomCompiler.Library("DocumentedControls", """
            namespace DocumentedControls;

            public sealed class DocumentedControl : Kolvir.Control
            {
                public string Title { get; set; } = "pristine";
            }
            """, referencesKolvir: true);

        // Same content, filed under another assembly's name: the index answers by assembly, so a file
        // that documents nothing the compilation references contributes nothing.
        var stray = new InMemoryAdditionalText("Unrelated.xml", """
            <?xml version="1.0"?>
            <doc>
                <assembly>
                    <name>Unrelated</name>
                </assembly>
                <members>
                    <member name="P:DocumentedControls.DocumentedControl.Title">
                        <summary>This must not be picked up.</summary>
                    </member>
                </members>
            </doc>
            """);

        var generated = VirtualDomCompiler.Build(
            DocumentationMode.Diagnose,
            [stray],
            [library],
            """
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(DocumentedControls.DocumentedControl))]
            internal partial class App;
            """);

        Assert.IsFalse(generated.Source.Contains("must not be picked up", StringComparison.Ordinal));
        AssertUndocumented(generated.Source, "Title");
    }

    /// <summary>Asserts that <paramref name="member"/>'s declaration is preceded by a doc comment.</summary>
    private static void AssertDocumented(string source, string member)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        int index = Array.FindIndex(lines, line => line.TrimStart().StartsWith("public ", StringComparison.Ordinal)
            && (line.TrimEnd().EndsWith(" " + member, StringComparison.Ordinal) || line.TrimEnd().EndsWith(" " + member + "{", StringComparison.Ordinal)));

        Assert.IsGreaterThanOrEqualTo(0, index, member + " is not in the generated source");

        var previous = lines[index - 1].TrimStart();

        if (previous.StartsWith('{'))
        {
            // An accessor line sits between the comment and the member for a property with a body.
            previous = lines[index - 2].TrimStart();
        }

        Assert.IsTrue(
            previous.StartsWith("///", StringComparison.Ordinal),
            member + " should carry a doc comment, and the line above it is: " + previous);
    }

    /// <summary>Asserts that <paramref name="member"/>'s declaration carries no doc comment.</summary>
    private static void AssertUndocumented(string source, string member)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        int index = Array.FindIndex(lines, line => line.TrimStart().StartsWith("public ", StringComparison.Ordinal)
            && line.TrimEnd().EndsWith(" " + member, StringComparison.Ordinal));

        Assert.IsGreaterThanOrEqualTo(0, index, member + " is not in the generated source");
        Assert.IsFalse(
            lines[index - 1].TrimStart().StartsWith("///", StringComparison.Ordinal),
            member + " should not carry a doc comment");
        Assert.IsFalse(
            lines[index - 2].TrimStart().StartsWith("///", StringComparison.Ordinal),
            member + " should not carry a doc comment");
    }
}

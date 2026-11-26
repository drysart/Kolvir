using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Feature.VirtualDom.Generators.Tests;

/// <summary>
/// The tripwire that stands in for a name the generator could not resolve. A conflict is a fact about
/// the application, so it is reported where the name is used and nowhere else: a placeholder nobody
/// names costs nothing, which is the whole reason it is a placeholder rather than an error.
/// </summary>
[TestClass]
public sealed class ConflictAnalyzerTests
{
    private const string TwoClaims = """
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
        """;

    [TestMethod]
    public async Task UsingAConflictingMemberIsReportedOnceWithBothClaimsAndTheFix()
    {
        var generated = VirtualDomCompiler.Build(TwoClaims, """
            namespace TestApp;

            public static class View
            {
                public static Kolvir.Feature.VirtualDom.VNode Render() => new VHostControl { Column = 3 };
            }
            """);

        var diagnostics = await DiagnosticsOfAsync(generated, "VD013");

        Assert.AreEqual(1, diagnostics.Length);

        var message = diagnostics[0].GetMessage();

        StringAssert.Contains(message, "'Column'");
        StringAssert.Contains(message, "TestApp.FirstExtensions::Column");
        StringAssert.Contains(message, "TestApp.SecondExtensions::Column");

        // Paste-ready, one per claimant, with the name left for the reader to choose.
        StringAssert.Contains(message, "[assembly: VNodeExtensionPropertyName(typeof(TestApp.FirstExtensions), \"Column\", \"<new name>\")]");
        StringAssert.Contains(message, "[assembly: VNodeExtensionPropertyName(typeof(TestApp.SecondExtensions), \"Column\", \"<new name>\")]");
        StringAssert.Contains(message, "resolving either claim resolves the conflict", message);
    }

    [TestMethod]
    public async Task AConflictingMemberNobodyUsesIsNotReported()
    {
        var generated = VirtualDomCompiler.Build(TwoClaims, """
            namespace TestApp;

            public static class View
            {
                public static Kolvir.Feature.VirtualDom.VNode Render() => new VHostControl();
            }
            """);

        Assert.IsEmpty(await DiagnosticsOfAsync(generated, "VD013"));
    }

    [TestMethod]
    public async Task ACompoundAssignmentIsReportedToo()
    {
        // `+=` wraps a property reference, so the one registration covers it without a second action.
        var generated = VirtualDomCompiler.Build("""
            using System;
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
            }

            public static class FirstExtensions
            {
                public static void AddSpunHandler(this HostControl control, Action handler)
                {
                }

                public static void RemoveSpunHandler(this HostControl control, Action handler)
                {
                }
            }

            public static class SecondExtensions
            {
                public static void AddSpunHandler(this HostControl control, Action handler)
                {
                }

                public static void RemoveSpunHandler(this HostControl control, Action handler)
                {
                }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """, """
            namespace TestApp;

            public static class View
            {
                public static Kolvir.Feature.VirtualDom.VNode Render()
                {
                    var node = new VHostControl();

                    node.Spun += _ => { };

                    return node;
                }
            }
            """);

        Assert.AreEqual(1, (await DiagnosticsOfAsync(generated, "VD013")).Length);
    }

    [TestMethod]
    public async Task AnOverrideOnEitherClaimResolvesTheConflict()
    {
        foreach (var container in new[] { "FirstExtensions", "SecondExtensions" })
        {
            var generated = VirtualDomCompiler.Build($$"""
                using Kolvir;
                using Kolvir.Feature.VirtualDom;

                [assembly: VNodeExtensionPropertyName(typeof(TestApp.{{container}}), "Column", "GridColumn")]

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
                """, """
                namespace TestApp;

                public static class View
                {
                    public static Kolvir.Feature.VirtualDom.VNode Render() =>
                        new VHostControl { Column = 3, GridColumn = 4 };
                }
                """);

            Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
            Assert.IsEmpty(await DiagnosticsOfAsync(generated, "VD013"));
            Assert.IsFalse(generated.Source.Contains("VNodeNameConflict", StringComparison.Ordinal), generated.Source);
            StringAssert.Contains(generated.Source, "public int Column");
            StringAssert.Contains(generated.Source, "public int GridColumn");
        }
    }

    [TestMethod]
    public async Task UsingAConflictingNodeNameIsReportedWithBothRegistrations()
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
            """, """
            namespace TestApp;

            public static class View
            {
                public static object Render() => new VButton();
            }
            """);

        var diagnostics = await DiagnosticsOfAsync(generated, "VD014");

        Assert.AreEqual(1, diagnostics.Length);

        var message = diagnostics[0].GetMessage();

        StringAssert.Contains(message, "'VButton'");
        StringAssert.Contains(message, "Acme.Button");
        StringAssert.Contains(message, "Widgets.Button");
        StringAssert.Contains(message, "[assembly: VirtualDom(typeof(Acme.Button), Name = \"<new name>\")]");
        StringAssert.Contains(message, "resolving either claim resolves the conflict", message);
    }

    [TestMethod]
    public async Task AConflictingNodeNameNobodyUsesIsNotReported()
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

                public static class View
                {
                    public static object Render() => new object();
                }
            }
            """);

        Assert.IsEmpty(await DiagnosticsOfAsync(generated, "VD014"));
    }

    private static async Task<Diagnostic[]> DiagnosticsOfAsync(Generated generated, string id)
    {
        ImmutableArray<Diagnostic> diagnostics = await VirtualDomCompiler.AnalyzeAsync(generated);

        return [.. diagnostics.Where(diagnostic => diagnostic.Id == id)];
    }
}


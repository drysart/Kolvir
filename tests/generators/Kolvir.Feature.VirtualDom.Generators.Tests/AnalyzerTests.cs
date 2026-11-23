using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Feature.VirtualDom.Generators.Tests;

/// <summary>
/// The two mistakes a rendered tree can make that the compiler only warns about, or misses entirely.
/// Every case here assigns something the build has to refuse, and each is paired with the case it must
/// leave alone - an analyzer that fires on correct code is worse than no analyzer.
/// </summary>
[TestClass]
public sealed class AnalyzerTests
{
    [TestMethod]
    public async Task ANullForANonNullablePropertyIsAnError()
    {
        var diagnostics = await RunAsync("""
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(HostControl))]
            internal partial class App;

            public static class View
            {
                public static VNode Render() => new VHostControl { Title = null };
            }
            """);

        CollectionAssert.Contains(Ids(diagnostics), "VD010");
    }

    [TestMethod]
    public async Task ANullForANonNullablePropertyIsAnErrorWithoutNullableAnalysisToo()
    {
        // The reason the analyzer exists: with the nullable context off, the compiler says nothing at
        // all, and the null would be stored and then thrown from the control's setter.
        var diagnostics = await RunAsync("""
            #nullable disable
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(HostControl))]
            internal partial class App;

            public static class View
            {
                public static VNode Render() => new VHostControl { Title = null };
            }
            """);

        CollectionAssert.Contains(Ids(diagnostics), "VD010");
    }

    [TestMethod]
    public async Task APossiblyNullValueForANonNullablePropertyIsAnError()
    {
        var diagnostics = await RunAsync("""
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(HostControl))]
            internal partial class App;

            public static class View
            {
                public static VNode Render(string? maybe) => new VHostControl { Title = maybe };
            }
            """);

        CollectionAssert.Contains(Ids(diagnostics), "VD010");
    }

    [TestMethod]
    public async Task AnAssignmentWrittenOutAsAStatementIsCheckedToo()
    {
        var diagnostics = await RunAsync("""
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(HostControl))]
            internal partial class App;

            public static class View
            {
                public static VNode Render()
                {
                    var node = new VHostControl();
                    node.Title = null;
                    return node;
                }
            }
            """);

        CollectionAssert.Contains(Ids(diagnostics), "VD010");
    }

    [TestMethod]
    public async Task ANullOverruledWithTheSuppressionOperatorIsLeftAlone()
    {
        // `null!` is the caller telling the analysis it knows better. The control's own setter is the
        // backstop at run time, and the build is not the place to argue.
        var diagnostics = await RunAsync("""
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(HostControl))]
            internal partial class App;

            public static class View
            {
                public static VNode Render() => new VHostControl { Title = null! };
            }
            """);

        CollectionAssert.DoesNotContain(Ids(diagnostics), "VD010");
    }

    [TestMethod]
    public async Task SettingAValueIsLeftAlone()
    {
        var diagnostics = await RunAsync("""
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(HostControl))]
            internal partial class App;

            public static class View
            {
                public static VNode Render() => new VHostControl { Title = "a title" };
            }
            """);

        CollectionAssert.DoesNotContain(Ids(diagnostics), "VD010");
    }

    [TestMethod]
    public async Task ANullablePropertyAcceptsNull()
    {
        // The mirror image: where the control's property is nullable, assigning null is a real
        // instruction and the analyzer must not stand in its way.
        var diagnostics = await RunAsync("""
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class OptionalControl : Kolvir.Control
            {
                public string? Note { get; set; }
            }

            [VirtualDom(typeof(OptionalControl))]
            internal partial class App;

            public static class View
            {
                public static VNode Render() => new VOptionalControl { Note = null };
            }
            """);

        CollectionAssert.DoesNotContain(Ids(diagnostics), "VD010");
    }

    [TestMethod]
    public async Task ChildrenOnALeafAreAnError()
    {
        var diagnostics = await RunAsync("""
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(HostControl))]
            internal partial class App;

            public static class View
            {
                public static VNode Render() => new VHostControl
                {
                    Children = [new VHostControl { Title = "inner" }],
                };
            }
            """);

        CollectionAssert.Contains(Ids(diagnostics), "VD011");
    }

    [TestMethod]
    public async Task ChildrenOnAContainerAreLeftAlone()
    {
        // Panel overrides OnLayout, which is what the generator reads to decide a node is not a leaf.
        var diagnostics = await RunAsync("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(Panel))]
            internal partial class App;

            public static class View
            {
                public static VNode Render() => new VPanel
                {
                    Children = [new VPanel()],
                };
            }
            """);

        CollectionAssert.DoesNotContain(Ids(diagnostics), "VD011");
    }

    [TestMethod]
    public async Task NoChildrenAtAllIsLeftAlone()
    {
        // `Children = []` says precisely what not assigning it says, so there is nothing to report.
        var diagnostics = await RunAsync("""
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(HostControl))]
            internal partial class App;

            public static class View
            {
                public static VNode Render() => new VHostControl { Children = [] };
            }
            """);

        CollectionAssert.DoesNotContain(Ids(diagnostics), "VD011");
    }

    private static async Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> RunAsync(string view)
    {
        var generated = VirtualDomCompiler.Build(Sources.HostControl, view);

        Assert.IsEmpty(generated.CompileErrors, "the test's own source has to compile: " + string.Join("\n", generated.CompileErrors));

        return await VirtualDomCompiler.AnalyzeAsync(generated);
    }

    private static string[] Ids(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics) =>
        [.. diagnostics.Select(diagnostic => diagnostic.Id)];
}

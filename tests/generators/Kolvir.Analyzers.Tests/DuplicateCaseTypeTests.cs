using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Analyzers.Tests;

/// <summary>
/// A union type with a repeated case. The collision is a property of the type itself, so the rule
/// reports every place the generic name is written rather than only the first.
/// </summary>
[TestClass]
public sealed class DuplicateCaseTypeTests
{
    [TestMethod]
    public async Task ATwoCaseUnionWithARepeatIsReported()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public sealed class Holder
            {
                public Union<int, int> Value;
            }
            """);

        Assert.AreEqual(1, diagnostics.Length, Describe(diagnostics));
        Assert.AreEqual("KOLVIR0003", diagnostics[0].Id);
    }

    [TestMethod]
    public async Task ThreeAndFourCaseRepeatsAreReported()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public sealed class Holder
            {
                public Union<int, string, int> Triple;
                public Union<int, string, bool, string> Quadruple;
            }
            """);

        CollectionAssert.AreEquivalent(
            new[] { "KOLVIR0003", "KOLVIR0003" },
            Ids(diagnostics));
    }

    [TestMethod]
    public async Task EveryUseOfTheGenericNameIsReported()
    {
        var diagnostics = await RunAsync("""
            using System;
            using Kolvir;

            namespace TestApp;

            using Pair = Union<int, int>;

            public sealed class Holder
            {
                public Union<int, int> Value;
                public Type Shape => typeof(Union<int, int>);
            }
            """);

        Assert.AreEqual(3, diagnostics.Length, Describe(diagnostics));
        Assert.IsTrue(diagnostics.All(diagnostic => diagnostic.Id == "KOLVIR0003"));
    }

    [TestMethod]
    public async Task XmlDocumentationCrefsAreExamplesNotCode()
    {
        var diagnostics = await RunAsync("""
            namespace TestApp;

            /// <summary>Never write <see cref="Kolvir.Union{int,int}"/>.</summary>
            /// <exception cref="Kolvir.Union{int,int}">Never.</exception>
            public sealed class Holder
            {
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task OrdinaryCommentsAreNotCode()
    {
        var diagnostics = await RunAsync("""
            namespace TestApp;

            // Union<int, int> is the bad shape.
            /* Union<int, int> is still the bad shape. */
            public sealed class Holder
            {
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task RealCodeWithTheSameTextStillFires()
    {
        var diagnostics = await RunAsync("""
            using System;
            using Kolvir;

            namespace TestApp;

            public sealed class Holder
            {
                public Union<int, int> Value;
                public Type Shape => typeof(Union<int, int>);
                public string Name => nameof(Union<int, int>);
            }
            """);

        Assert.AreEqual(3, diagnostics.Length, Describe(diagnostics));
        Assert.IsTrue(diagnostics.All(diagnostic => diagnostic.Id == "KOLVIR0003"));
    }

    [TestMethod]
    public async Task NullableAnnotationsOnTheSameRuntimeTypeAreARepeat()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public sealed class Holder
            {
                public Union<string, string?> Value;
            }
            """);

        Assert.AreEqual(1, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task DynamicAndObjectAreTheSameRuntimeType()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public sealed class Holder
            {
                public Union<dynamic, object> Value;
            }
            """);

        Assert.AreEqual(1, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task DistinctCasesAreLeftAlone()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public sealed class Holder
            {
                public Union<int, string> Pair;
                public Union<int, string, bool> Triple;
                public Union<int, string, bool, System.Guid> Quadruple;
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task OpenGenericParametersAreLeftAlone()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public static class Holder
            {
                public static void Use<TFirst, TSecond>(Union<TFirst, TSecond> value)
                {
                }
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task TheUnboundGenericIsLeftAlone()
    {
        var diagnostics = await RunAsync("""
            using System;
            using Kolvir;

            namespace TestApp;

            public static class Holder
            {
                public static Type Shape => typeof(Union<,>);
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task ATypesOwnUnionIsNotKolvirUnion()
    {
        var diagnostics = await RunAsync("""
            namespace TestApp;

            public readonly struct Union<TFirst, TSecond>
            {
            }

            public sealed class Holder
            {
                public Union<int, int> Value;
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task AnUnresolvedCaseTypeIsLeftAlone()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public sealed class Holder
            {
                public Union<int, Missing> Value;
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task TheMessageNamesTheTypeAndTheRepeatedCase()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public sealed class Holder
            {
                public Union<int, int> Value;
            }
            """);

        var message = diagnostics[0].GetMessage();
        StringAssert.Contains(message, "Union<int, int>");
        StringAssert.Contains(message, "int");
    }

    private static Task<ImmutableArray<Diagnostic>> RunAsync(string source) =>
        AnalyzerCompiler.AnalyzeAsync(new DuplicateCaseTypeAnalyzer(), source);

    private static string[] Ids(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Select(diagnostic => diagnostic.Id).ToArray();

    private static string Describe(IEnumerable<Diagnostic> diagnostics) =>
        string.Join("\n", diagnostics.Select(diagnostic => diagnostic.ToString()));
}

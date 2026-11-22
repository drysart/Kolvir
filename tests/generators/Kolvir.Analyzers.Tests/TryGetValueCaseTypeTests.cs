using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Analyzers.Tests;

/// <summary>
/// The <c>TryGetValue</c> type argument. A type outside the union's case types compiles and throws
/// when the line runs, so every arity is covered together with the cases that must stay quiet.
/// </summary>
[TestClass]
public sealed class TryGetValueCaseTypeTests
{
    [TestMethod]
    public async Task ANonCaseTypeIsReportedOnEveryArity()
    {
        var diagnostics = await RunAsync("""
            using System;
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read(
                    Union<string, int> pair,
                    Union<string, int, bool> triple,
                    Union<string, int, bool, Guid> quadruple)
                {
                    pair.TryGetValue<Guid>(out _);
                    triple.TryGetValue<Guid>(out _);
                    quadruple.TryGetValue<double>(out _);
                }
            }
            """);

        Assert.AreEqual(3, diagnostics.Length, Describe(diagnostics));
        Assert.IsTrue(diagnostics.All(diagnostic => diagnostic.Id == "KOLVIR0001"));
    }

    [TestMethod]
    public async Task EveryRealCaseTypeIsLeftAlone()
    {
        var diagnostics = await RunAsync("""
            using System;
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read(Union<string, int, bool, Guid> union)
                {
                    union.TryGetValue<string>(out _);
                    union.TryGetValue<int>(out _);
                    union.TryGetValue<bool>(out _);
                    union.TryGetValue<Guid>(out _);
                }
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task AnObjectCaseIsNotConfusedWithAssignableTypes()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read(Union<object, string> union)
                {
                    union.TryGetValue<object>(out _);
                    union.TryGetValue<string>(out _);
                }
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task AnUnresolvedTypeIsLeftAlone()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read(Union<string, int> union) => union.TryGetValue<Missing>(out _);
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task DynamicIsLeftAlone()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read(Union<string, int> union) => union.TryGetValue<dynamic>(out _);
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task ATypeParameterIsLeftAlone()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read<T>(Union<string, int> union) => union.TryGetValue<T>(out _);
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task AUnionWithATypeParameterCaseIsLeftAlone()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read<T>(Union<T, int> union) => union.TryGetValue<string>(out _);
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task ATryGetValueOnAnotherTypeIsLeftAlone()
    {
        var diagnostics = await RunAsync("""
            namespace TestApp;

            public sealed class Box
            {
                public bool TryGetValue<T>(out T value)
                {
                    value = default!;
                    return false;
                }
            }

            public static class Reader
            {
                public static void Read(Box box) => box.TryGetValue<string>(out _);
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task TheReportPointsAtTheTypeArgument()
    {
        const string Source = """
            using System;
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read(Union<string, int> union) => union.TryGetValue<Guid>(out _);
            }
            """;

        var diagnostics = await AnalyzerCompiler.AnalyzeAsync(new TryGetValueCaseTypeAnalyzer(), Source);

        Assert.AreEqual(1, diagnostics.Length, Describe(diagnostics));
        var start = Source.IndexOf("Guid", StringComparison.Ordinal);
        Assert.AreEqual(start, diagnostics[0].Location.SourceSpan.Start);
    }

    [TestMethod]
    public async Task AnInferredTypeArgumentFallsBackToTheInvocation()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read(Union<string, int> union)
                {
                    object value;
                    union.TryGetValue(out value);
                }
            }
            """);

        Assert.AreEqual(1, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task TheMessageNamesTheBadTypeTheCasesAndInherit()
    {
        var diagnostics = await RunAsync("""
            using System;
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read(Union<string, int, InheritedValue> union) => union.TryGetValue<Guid>(out _);
            }
            """);

        var message = diagnostics[0].GetMessage();
        StringAssert.Contains(message, "Guid");
        StringAssert.Contains(message, "string");
        StringAssert.Contains(message, "int");
        StringAssert.Contains(message, "InheritedValue");
        StringAssert.Contains(message, "PropertyValue.Inherited");
    }

    private static Task<ImmutableArray<Diagnostic>> RunAsync(string source) =>
        AnalyzerCompiler.AnalyzeAsync(new TryGetValueCaseTypeAnalyzer(), source);

    private static string Describe(IEnumerable<Diagnostic> diagnostics) =>
        string.Join("\n", diagnostics.Select(diagnostic => diagnostic.ToString()));
}

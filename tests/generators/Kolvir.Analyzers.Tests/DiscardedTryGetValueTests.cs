using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Analyzers.Tests;

/// <summary>
/// The discarded-result warning. A call used as a whole statement throws the bool away, while every
/// other use of it - assignment, condition, return, negation, argument, lambda body - is a use and
/// must stay quiet. The explicit <c>_ =</c> form is the documented way to say the discard is meant.
/// </summary>
[TestClass]
public sealed class DiscardedTryGetValueTests
{
    [TestMethod]
    public async Task ABareCallIsReported()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read(Union<string, int> union)
                {
                    union.TryGetValue<string>(out _);
                }
            }
            """);

        Assert.AreEqual(1, diagnostics.Length, Describe(diagnostics));
        Assert.AreEqual("KOLVIR0005", diagnostics[0].Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostics[0].Severity);
    }

    [TestMethod]
    public async Task AnOutVariableDeclaredByTheCallIsStillADiscardedResult()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read(Union<string, int> union)
                {
                    union.TryGetValue<string>(out var value);
                }
            }
            """);

        Assert.AreEqual(1, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task AnAssignedResultIsLeftAlone()
    {
        Assert.AreEqual(0, (await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read(Union<string, int> union)
                {
                    var found = union.TryGetValue<string>(out var value);
                }
            }
            """)).Length);
    }

    [TestMethod]
    public async Task AnExplicitDiscardIsLeftAlone()
    {
        Assert.AreEqual(0, (await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read(Union<string, int> union)
                {
                    _ = union.TryGetValue<string>(out _);
                }
            }
            """)).Length);
    }

    [TestMethod]
    public async Task AConditionReturnNegationAndArgumentAreLeftAlone()
    {
        var diagnostics = await RunAsync("""
            using System;
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static bool Read(Union<string, int> union)
                {
                    if (union.TryGetValue<string>(out _))
                    {
                    }

                    var negated = !union.TryGetValue<string>(out _);
                    Func<bool> deferred = () => union.TryGetValue<string>(out _);
                    Use(union.TryGetValue<string>(out _));
                    return union.TryGetValue<string>(out _);
                }

                private static void Use(bool value)
                {
                }
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
                public static void Read(Box box)
                {
                    box.TryGetValue<string>(out _);
                }
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task ANonCaseTypeIsLeftToKOLVIR0001()
    {
        var diagnostics = await RunAsync("""
            using System;
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read(Union<string, int> union)
                {
                    union.TryGetValue<Guid>(out _);
                }
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task TheMessageNamesTheTypeAndTheExplicitDiscard()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read(Union<string, int> union)
                {
                    union.TryGetValue<string>(out _);
                }
            }
            """);

        var message = diagnostics[0].GetMessage();
        StringAssert.Contains(message, "TryGetValue<string>");
        StringAssert.Contains(message, "_ =");
    }

    private static Task<ImmutableArray<Diagnostic>> RunAsync(string source) =>
        AnalyzerCompiler.AnalyzeAsync(new DiscardedTryGetValueAnalyzer(), source);

    private static string Describe(IEnumerable<Diagnostic> diagnostics) =>
        string.Join("\n", diagnostics.Select(diagnostic => diagnostic.ToString()));
}

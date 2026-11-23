using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Analyzers.Tests;

/// <summary>
/// The explicit ways to make a default-initialised union. The rule deliberately does not try to
/// find a field or property that is merely never assigned; these are the forms it can see.
/// </summary>
[TestClass]
public sealed class DefaultUnionTests
{
    [TestMethod]
    public async Task ATargetTypedDefaultIsReported()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public sealed class Holder
            {
                public Union<string, int> Value = default;
            }
            """);

        Assert.AreEqual(1, diagnostics.Length, Describe(diagnostics));
        Assert.AreEqual("KOLVIR0002", diagnostics[0].Id);
    }

    [TestMethod]
    public async Task ADefaultOfTheClosedUnionIsReported()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read() => Use(default(Union<string, int>));

                private static void Use(Union<string, int> value)
                {
                }
            }
            """);

        Assert.AreEqual(1, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task ATargetTypedDefaultPassedAsAnArgumentIsReported()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public static class Reader
            {
                public static void Read() => Use(default);

                private static void Use(Union<string, int> value)
                {
                }
            }
            """);

        Assert.AreEqual(1, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task AParameterlessNewIsReported()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public sealed class Holder
            {
                public Union<string, int> Value = new Union<string, int>();
            }
            """);

        Assert.AreEqual(1, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task AnUninitialisedFieldIsLeftAlone()
    {
        // The rule has no dataflow. A struct defaults silently, and finding the field that was
        // never assigned is deliberately somebody else's problem.
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public sealed class Holder
            {
                public Union<string, int> Value;
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task DefaultsOfOtherTypesAreLeftAlone()
    {
        var diagnostics = await RunAsync("""
            namespace TestApp;

            public sealed class Holder
            {
                public int Number = default;
                public string? Text = default;
                public int[] Values = default(int[]);
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task ANewUnionWithACaseIsLeftAlone()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public sealed class Holder
            {
                public Union<string, int> Value = new Union<string, int>("case");
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task AnUnresolvedUnionIsLeftAlone()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public sealed class Holder
            {
                public Union<string, Missing> Value = default;
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task TheFieldResetInsideAUnionConstructorIsLeftAlone()
    {
        var diagnostics = await AnalyzerCompiler.AnalyzeAsync(new DefaultUnionAnalyzer(), false, """
            namespace Kolvir
            {
                public readonly struct Union<TFirst, TSecond>
                {
                    private readonly TFirst _first;

                    public Union(TFirst value)
                    {
                        this = default;
                        _first = value;
                    }

                    public TFirst First => _first;
                }
            }
            """);

        Assert.AreEqual(0, diagnostics.Length, Describe(diagnostics));
    }

    [TestMethod]
    public async Task TheMessageNamesTheUnionAndTheInheritSpelling()
    {
        var diagnostics = await RunAsync("""
            using Kolvir;

            namespace TestApp;

            public sealed class Holder
            {
                public Union<string, int> Value = default;
            }
            """);

        var message = diagnostics[0].GetMessage();
        StringAssert.Contains(message, "default-initialised");
        StringAssert.Contains(message, "Union<string, int>");
        StringAssert.Contains(message, "PropertyValue.Inherited");
    }

    private static Task<ImmutableArray<Diagnostic>> RunAsync(string source) =>
        AnalyzerCompiler.AnalyzeAsync(new DefaultUnionAnalyzer(), source);

    private static string Describe(IEnumerable<Diagnostic> diagnostics) =>
        string.Join("\n", diagnostics.Select(diagnostic => diagnostic.ToString()));
}

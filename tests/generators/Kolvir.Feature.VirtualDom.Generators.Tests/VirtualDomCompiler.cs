using System.Collections.Immutable;
using Kolvir.VirtualDom.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kolvir.VirtualDom.Generators.Tests;

/// <summary>
/// Builds a compilation from source, runs the generator over it, and hands back everything a test
/// needs to assert on: the emitted code, the generator's diagnostics and the analyzer's.
/// </summary>
/// <remarks>
/// The references are the assemblies this test process is running against rather than a folder of
/// reference assemblies: the tests only compile and analyze, never load or run what they compile, so
/// the implementation assemblies are the honest thing to compile against - and they are the same
/// Kolvir and SkiaSharp the repository just built.
/// </remarks>
internal static class VirtualDomCompiler
{
    private static readonly ImmutableArray<MetadataReference> References = GatherReferences();

    private static readonly CSharpParseOptions Options = new(LanguageVersion.Preview);

    /// <summary>Compiles <paramref name="sources"/> and runs the generator over the result.</summary>
    /// <param name="sources">The test's own code, one string per file, which is where a manifest is declared.</param>
    /// <param name="nullable">Whether the nullable context is on, so that a file can pretend not to be.</param>
    /// <returns>The result, with the generated code already part of the compilation.</returns>
    internal static Generated Build(params string[] sources) => Build(NullableContextOptions.Enable, sources);

    /// <summary>Compiles <paramref name="sources"/> under a chosen nullable context.</summary>
    /// <param name="nullable">Whether the nullable context is on.</param>
    /// <param name="sources">The test's own code, one string per file.</param>
    /// <returns>The result, with the generated code already part of the compilation.</returns>
    internal static Generated Build(NullableContextOptions nullable, params string[] sources)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "VirtualDomUnderTest",
            syntaxTrees: [.. sources.Select(source => CSharpSyntaxTree.ParseText(source, Options))],
            references: References,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: nullable));

        var driver = CSharpGeneratorDriver.Create(new VirtualDomGenerator()).WithUpdatedParseOptions(Options);

        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var output,
            out _);

        return new Generated(compilation, output, driver.GetRunResult());
    }

    /// <summary>Runs the analyzer over an already-generated compilation, as the compiler would.</summary>
    /// <param name="generated">The compilation the generator has been run over.</param>
    /// <returns>The analyzer's diagnostics, only: everything else is the compiler's business.</returns>
    internal static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(Generated generated)
    {
        var withAnalyzers = generated.Output.WithAnalyzers([new NullAndLeafAnalyzer()]);
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

        return diagnostics;
    }

    private static ImmutableArray<MetadataReference> GatherReferences()
    {
        var paths = new List<string>();

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string platform)
        {
            paths.AddRange(platform.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (var assembly in new[] { typeof(Control).Assembly, typeof(VNode).Assembly })
        {
            if (!paths.Contains(assembly.Location, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(assembly.Location);
            }
        }

        return
        [
            .. paths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path)),
        ];
    }
}

/// <summary>What a test compilation produced.</summary>
/// <param name="Input">The compilation as it was written, before generation.</param>
/// <param name="Output">The compilation with the generated trees added.</param>
/// <param name="DriverResult">What the generator emitted, and what it reported.</param>
internal sealed record Generated(CSharpCompilation Input, Compilation Output, GeneratorDriverRunResult DriverResult)
{
    /// <summary>The generated code, by hint name.</summary>
    internal IEnumerable<(string HintName, string Source)> Sources =>
        DriverResult.Results
            .SelectMany(result => result.GeneratedSources)
            .Select(source => (source.HintName, source.SourceText.ToString()));

    /// <summary>The generated code as one string, for a test that only needs to look for members.</summary>
    internal string Source => string.Join("\n", Sources.Select(source => source.Source));

    /// <summary>Every diagnostic the generator reported, by id.</summary>
    internal string[] Ids => DriverResult.Diagnostics
        .Where(diagnostic => diagnostic.Id.StartsWith("VD", StringComparison.Ordinal))
        .Select(diagnostic => diagnostic.Id)
        .ToArray();

    /// <summary>The errors in the output compilation, which for a well-formed manifest should be none.</summary>
    internal string[] CompileErrors => Output.GetDiagnostics()
        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        .Select(diagnostic => diagnostic.ToString())
        .ToArray();
}

/// <summary>A source that declares a control of its own, so a test is not tied to a built-in one.</summary>
internal static class Sources
{
    /// <summary>
    /// A control with a non-nullable string, which is what the null analyzer is aimed at, and no
    /// <c>OnLayout</c> override, which makes its node a leaf.
    /// </summary>
    internal const string HostControl = """
        namespace TestApp
        {
            public sealed class HostControl : Kolvir.Control
            {
                public string Title { get; set; } = "pristine";
            }
        }
        """;
}

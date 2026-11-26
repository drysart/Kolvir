using System.Collections.Immutable;
using Kolvir.Feature.VirtualDom.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kolvir.Feature.VirtualDom.Generators.Tests;

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
    private static readonly ImmutableArray<string> ReferencePaths = GatherPaths();

    private static readonly ImmutableArray<MetadataReference> References =
        [.. ReferencePaths.Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path))];

    private static readonly CSharpParseOptions Options = new(LanguageVersion.Preview);

    /// <summary>Compiles <paramref name="sources"/> and runs the generator over the result.</summary>
    /// <param name="sources">The test's own code, one string per file, which is where a manifest is declared.</param>
    /// <param name="nullable">Whether the nullable context is on, so that a file can pretend not to be.</param>
    /// <returns>The result, with the generated code already part of the compilation.</returns>
    internal static Generated Build(params string[] sources) => Build(NullableContextOptions.Enable, [], sources);

    /// <summary>Compiles <paramref name="sources"/> under a chosen nullable context.</summary>
    /// <param name="nullable">Whether the nullable context is on.</param>
    /// <param name="sources">The test's own code, one string per file.</param>
    /// <returns>The result, with the generated code already part of the compilation.</returns>
    internal static Generated Build(NullableContextOptions nullable, params string[] sources) => Build(nullable, [], sources);

    /// <summary>Compiles <paramref name="sources"/> with extra references as well as the standard ones.</summary>
    /// <param name="extra">References the test compiled itself, such as a library that declares an extension.</param>
    /// <param name="sources">The test's own code, one string per file.</param>
    /// <returns>The result, with the generated code already part of the compilation.</returns>
    internal static Generated Build(IEnumerable<MetadataReference> extra, params string[] sources) =>
        Build(NullableContextOptions.Enable, extra, sources);

    private static Generated Build(NullableContextOptions nullable, IEnumerable<MetadataReference> extra, string[] sources)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "VirtualDomUnderTest",
            syntaxTrees: [.. sources.Select(source => CSharpSyntaxTree.ParseText(source, Options))],
            references: [.. References, .. extra],
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

    /// <summary>
    /// Compiles a small library to a reference, so a test can put an extension member in an assembly of
    /// its own - with or without a reference to Kolvir, which is what the scan's narrowing turns on.
    /// </summary>
    /// <param name="name">The assembly name.</param>
    /// <param name="source">The library's code.</param>
    /// <param name="referencesKolvir">Whether the library references the assembly that declares Control.</param>
    /// <returns>The reference, ready to pass to <see cref="Build(IEnumerable{MetadataReference}, string[])"/>.</returns>
    internal static MetadataReference Library(string name, string source, bool referencesKolvir)
    {
        var control = typeof(Control).Assembly.Location;

        var paths = ReferencePaths
            .Where(path => referencesKolvir || !string.Equals(path, control, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var compilation = CSharpCompilation.Create(
            assemblyName: name,
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, Options)],
            references: [.. paths.Select(path => MetadataReference.CreateFromFile(path))],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var image = new System.IO.MemoryStream();
        var result = compilation.Emit(image);

        Assert.IsTrue(
            result.Success,
            "the library did not compile: "
                + string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));

        return MetadataReference.CreateFromImage(image.ToArray());
    }

    /// <summary>Runs the analyzers over an already-generated compilation, as the compiler would.</summary>
    /// <param name="generated">The compilation the generator has been run over.</param>
    /// <returns>The analyzers' diagnostics, only: everything else is the compiler's business.</returns>
    internal static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(Generated generated)
    {
        var withAnalyzers = generated.Output.WithAnalyzers([new NullAndLeafAnalyzer(), new NodeNameConflictAnalyzer()]);
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

        return diagnostics;
    }

    private static ImmutableArray<string> GatherPaths()
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

        return [.. paths];
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

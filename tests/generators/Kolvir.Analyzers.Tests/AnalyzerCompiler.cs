using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kolvir.Analyzers.Tests;

/// <summary>
/// Compiles a source string and runs an analyzer over it, the way the compiler would. The
/// references are the assemblies this test process is running against plus Kolvir and SkiaSharp;
/// the tests only compile and analyze, never load or run what they compile.
/// </summary>
internal static class AnalyzerCompiler
{
    private static readonly ImmutableArray<MetadataReference> PlatformReferences = GatherPlatformReferences();
    private static readonly CSharpParseOptions Options = new(LanguageVersion.Preview);

    /// <summary>Analyzes <paramref name="sources"/> with Kolvir available as a metadata reference.</summary>
    internal static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(DiagnosticAnalyzer analyzer, params string[] sources) =>
        AnalyzeAsync(analyzer, includeKolvir: true, sources);

    /// <summary>
    /// Analyzes <paramref name="sources"/> with or without the real Kolvir assembly, so a test can
    /// declare a stub <c>Kolvir.Union&lt;...&gt;</c> without colliding with the real one.
    /// </summary>
    internal static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        DiagnosticAnalyzer analyzer,
        bool includeKolvir,
        params string[] sources)
    {
        var references = includeKolvir
            ? PlatformReferences.AddRange(KolvirReferences())
            : PlatformReferences;

        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerUnderTest",
            syntaxTrees: sources.Select(source => CSharpSyntaxTree.ParseText(source, Options)),
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var withAnalyzers = compilation.WithAnalyzers([analyzer]);
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static IEnumerable<MetadataReference> KolvirReferences() =>
        new[]
        {
            typeof(Kolvir.Union<,>).Assembly,
            typeof(SkiaSharp.SKColor).Assembly,
        }.Select(assembly => MetadataReference.CreateFromFile(assembly.Location));

    private static ImmutableArray<MetadataReference> GatherPlatformReferences()
    {
        var paths = new List<string>();

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string platform)
        {
            paths.AddRange(platform.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        }

        return
        [
            .. paths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path)),
        ];
    }
}

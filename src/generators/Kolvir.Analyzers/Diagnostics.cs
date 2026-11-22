using Microsoft.CodeAnalysis;

namespace Kolvir.Analyzers;

/// <summary>The diagnostics the union analyzers report.</summary>
internal static class Diagnostics
{
    private const string Category = "Kolvir.Usage";

    internal static readonly DiagnosticDescriptor DuplicateCaseType = new(
        "KOLVIR0003",
        "Union has duplicate case types",
        "'{0}' has duplicate case types ({1}); the constructors and conversion operators collide, so the union cannot be used",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor TryGetValueCaseType = new(
        "KOLVIR0001",
        "TryGetValue type is not a case of this union",
        "'{0}' is not a case type of {1}, so TryGetValue<{0}> cannot answer and throws ArgumentException at run time; ask for one of {2}{3}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}

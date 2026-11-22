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
}

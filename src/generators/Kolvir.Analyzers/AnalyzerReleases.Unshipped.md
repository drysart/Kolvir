; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
;
; The identifier of a rule is part of the package's interface once it ships: an application that
; silences KOLVIR0001 in its own editorconfig has to keep being silenced. This file is the record of
; what has not been published yet, which is why a rule is added here before it is added to a release.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------

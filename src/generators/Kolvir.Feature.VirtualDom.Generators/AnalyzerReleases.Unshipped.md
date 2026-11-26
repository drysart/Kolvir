; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
;
; The identifier of a rule is part of the package's interface once it ships: an application that
; silences VD010 in its own editorconfig has to keep being silenced. This file is the record of what
; has not been published yet, which is why a rule is added here before it is added to a release.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
VD001 | VirtualDom | Error | Registered type is not a control
VD002 | VirtualDom | Error | Registered control has no accessible parameterless constructor
VD004 | VirtualDom | Error | The same control is registered more than once
VD005 | VirtualDom | Error | The registration's Name is not a valid C# identifier
VD007 | VirtualDom | Error | The class carrying the registrations is not partial
VD010 | VirtualDom | Error | A null is assigned to a node property that cannot hold one
VD011 | VirtualDom | Error | Children are assigned to a node whose control shows none
VD012 | VirtualDom | Error | A control property has the same name as a member of the node class
VD013 | VirtualDom | Error | Two extension members want the same node property
VD014 | VirtualDom | Error | Two controls want the same node class name
VD015 | VirtualDom | Error | An overridden extension member name is already taken

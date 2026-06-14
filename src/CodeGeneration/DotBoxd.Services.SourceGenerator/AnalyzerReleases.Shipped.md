; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DBXS001 | DotBoxd.Services.SourceGenerator | Error | DotBoxd source generator failure
DBXS002 | DotBoxd.Services.SourceGenerator | Error | Unsupported DotBoxd method shape (e.g. ref/in/out parameter)
DBXS003 | DotBoxd.Services.SourceGenerator | Error | Unsupported DotBoxd service shape (e.g. generic or nested interface)
DBXS004 | DotBoxd.Services.SourceGenerator | Warning | Async sibling interface method name collides with another method

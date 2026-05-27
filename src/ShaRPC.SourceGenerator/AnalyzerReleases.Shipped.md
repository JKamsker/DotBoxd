; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SHARPC001 | ShaRPC.SourceGenerator | Error | ShaRPC source generator failure
SHARPC002 | ShaRPC.SourceGenerator | Error | Unsupported ShaRPC method shape (e.g. ref/in/out parameter)
SHARPC003 | ShaRPC.SourceGenerator | Error | Unsupported ShaRPC service shape (e.g. generic or nested interface)
SHARPC004 | ShaRPC.SourceGenerator | Warning | Async sibling interface method name collides with another method

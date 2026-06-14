; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SGP100 | SafeIR.Generation | Error | Plugin kernel shape is not supported
SGP110 | SafeIR.Generation | Info | InvokeKernel(lambda) chain is not yet lowered to verified IR

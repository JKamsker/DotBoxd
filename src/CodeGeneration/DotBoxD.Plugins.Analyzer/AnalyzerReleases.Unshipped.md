; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DBXK100 | DotBoxD.Kernels.Generation | Error | Plugin kernel shape is not supported; string interpolation holes may be strings or supported invariant string-convertible numeric types
DBXK111 | DotBoxD.Kernels.Generation | Info | Remote RunLocal chain could not be lowered and will throw NotSupportedException at runtime
DBXK112 | DotBoxD.Kernels.Generation | Error | A [HookResult] record must declare a bool Success and a string? Reason field
DBXK113 | DotBoxD.Kernels.Generation | Info | Result hook Register/RegisterLocal chain could not be lowered and will throw at runtime (the un-lowered sandbox Register case is raised to Warning at the call site since it has no in-process fallback)
DBXK114 | DotBoxD.Kernels.Generation | Warning | Run chain could not be lowered and will throw DBXK062 at runtime
DBXK115 | DotBoxD.Kernels.Generation | Error | Duplicate generated server-extension graft signatures are rejected
DBXK116 | DotBoxD.Kernels.Generation | Error | [Local] context helpers are rejected outside declared contexts and from lowered server-side IR

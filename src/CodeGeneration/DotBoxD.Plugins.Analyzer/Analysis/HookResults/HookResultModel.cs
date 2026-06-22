namespace DotBoxD.Plugins.Analyzer.Analysis.HookResults;

/// <summary>
/// The equatable shape of a <c>[HookResult]</c> record the builder generator emits members for: its
/// namespace/name, the partial declaration keywords to mirror, its positional fields, the builder member
/// names the user already declared (so they are not re-emitted), and whether it satisfies the
/// <c>bool Success</c> / <c>string? Reason</c> contract. Pure primitives only — no Roslyn symbols — so the
/// incremental pipeline caches it.
/// </summary>
internal sealed record HookResultModel(
    string? Namespace,
    string TypeName,
    string DeclarationKeywords,
    EquatableArray<HookResultField> Fields,
    EquatableArray<string> ExistingMembers,
    bool HasSuccess,
    bool HasReason,
    HookResultDiagnostic? Diagnostic);

/// <summary>One positional field of a hook-result record: its name, fully-qualified type, and whether it is
/// a control field (<c>Success</c>/<c>Reason</c>) managed by <c>Ok</c>/<c>Reject</c> rather than a
/// <c>With&lt;Field&gt;</c> domain setter.</summary>
internal sealed record HookResultField(string Name, string TypeFullName, string ParameterName, bool IsControl);

/// <summary>A build-time diagnostic the builder generator surfaces for a malformed <c>[HookResult]</c> type.</summary>
internal sealed record HookResultDiagnostic(PluginDiagnosticLocation Location, string Message)
{
    public Microsoft.CodeAnalysis.Diagnostic ToDiagnostic()
        => Microsoft.CodeAnalysis.Diagnostic.Create(
            PluginAnalyzerDiagnostics.HookResultContractRule,
            Location.ToLocation(),
            Message);
}

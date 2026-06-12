namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;

internal static class PluginAnalyzerDiagnostics
{
    public static readonly DiagnosticDescriptor UnsupportedKernelShapeRule = new(
        "SGP100",
        "Plugin kernel shape is not supported",
        "{0}",
        "SafeIR.Generation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Plugin package generation supports a restricted kernel expression subset.");
}

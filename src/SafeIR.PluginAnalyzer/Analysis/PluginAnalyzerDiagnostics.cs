namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;

internal static class PluginAnalyzerDiagnostics
{
    internal const string ShippedRulesHelpLinkBase =
        "https://github.com/JKamsker/Safe-IR/blob/main/src/SafeIR.PluginAnalyzer/AnalyzerReleases.Shipped.md#";

    internal const string UnshippedRulesHelpLinkBase =
        "https://github.com/JKamsker/Safe-IR/blob/main/src/SafeIR.PluginAnalyzer/AnalyzerReleases.Unshipped.md#";

    public static readonly DiagnosticDescriptor UnsupportedKernelShapeRule = new(
        "SGP100",
        "Plugin kernel shape is not supported",
        "{0}",
        "SafeIR.Generation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Plugin package generation supports a restricted kernel expression subset.",
        helpLinkUri: UnshippedRulesHelpLinkBase + "SGP100");
}

namespace DotBoxd.Plugins.Analyzer;

using Microsoft.CodeAnalysis;

internal static class PluginAnalyzerDiagnostics
{
    internal const string ShippedRulesHelpLinkBase =
        "https://github.com/JKamsker/Safe-IR/blob/main/src/DotBoxd.Plugins.Analyzer/AnalyzerReleases.Shipped.md#";

    internal const string UnshippedRulesHelpLinkBase =
        "https://github.com/JKamsker/Safe-IR/blob/main/src/DotBoxd.Plugins.Analyzer/AnalyzerReleases.Unshipped.md#";

    public static readonly DiagnosticDescriptor UnsupportedKernelShapeRule = new(
        "DBXK100",
        "Plugin kernel shape is not supported",
        "{0}",
        "DotBoxd.Kernels.Generation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Plugin package generation supports a restricted kernel expression subset.",
        helpLinkUri: UnshippedRulesHelpLinkBase + "DBXK100");
}

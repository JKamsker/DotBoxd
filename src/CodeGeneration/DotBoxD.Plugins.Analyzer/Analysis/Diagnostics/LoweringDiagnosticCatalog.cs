using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal sealed record LoweringDiagnosticCatalogEntry(
    string Surface,
    string FactoryTypeName,
    DiagnosticDescriptor Descriptor,
    string FailureRoute,
    string UnsupportedShapeFamily);

internal static class LoweringDiagnosticCatalog
{
    private static readonly LoweringDiagnosticCatalogEntry[] s_entries =
    [
        new(
            "event/plugin kernel class",
            "PluginKernelModelFactory",
            PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
            "NotSupportedException is caught at the model factory and reported as PluginKernelDiagnostic.",
            "IEventKernel lowering, event/live-setting shape, ShouldHandle, and Handle IR"),
        new(
            "server extension RPC kernel",
            "RpcKernelModelFactory",
            PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
            "NotSupportedException is caught at the model factory and reported as PluginKernelDiagnostic.",
            "[ServerExtension] method shape, RPC payloads, DTO construction, and host bindings"),
        new(
            "generated plugin server InvokeAsync",
            "InvokeAsyncModelFactory",
            PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
            "NotSupportedException is caught at the call-shape factory and reported as PluginKernelDiagnostic.",
            "InvokeAsync receiver, lambda/capture shape, RPC argument/result payloads"),
        new(
            "generated plugin server facade",
            "PluginServerFacadeModelFactory",
            PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
            "NotSupportedException is caught at the facade factory and reported as PluginKernelDiagnostic.",
            "[GeneratePluginServer] target, world/control contracts, generated facade surface"),
        new(
            "registration accumulator generator",
            "RegistrationAccumulatorModelFactory",
            PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
            "NotSupportedException is caught by the accumulator factory and reported as PluginKernelDiagnostic.",
            "registration accumulator target/root declarations and generated member names"),
        new(
            "remote RunLocal hook chain",
            "HookChainModelFactory",
            PluginAnalyzerDiagnostics.RunLocalNotLoweredRule,
            "Recognized remote RunLocal chains that fail lowering report HookChainNotLoweredDiagnostic.",
            "remote RunLocal Where/Select projection and predicate lowering"),
        new(
            "result hook Register/RegisterLocal chain",
            "HookChainModelFactory",
            PluginAnalyzerDiagnostics.ResultHookNotLoweredRule,
            "Recognized result hook chains that fail lowering report HookChainNotLoweredDiagnostic.",
            "result hook context, filter, handler, result-builder, and local handler lowering"),
        new(
            "remote/local Run hook chain",
            "HookChainModelFactory",
            PluginAnalyzerDiagnostics.RunChainNotLoweredRule,
            "Recognized Run chains that fail lowering report HookChainNotLoweredDiagnostic.",
            "Run Where/Select predicate, projection, and terminal body lowering"),
        new(
            "plugin source generator stage",
            "GeneratorGuard",
            PluginAnalyzerDiagnostics.SourceGeneratorFailureRule,
            "Unexpected generator exceptions are isolated to the affected stage and reported as DBXK117.",
            "last-resort guard for generator stages outside a deterministic unsupported-shape rule"),
    ];

    public static IReadOnlyList<LoweringDiagnosticCatalogEntry> Entries => s_entries;
}

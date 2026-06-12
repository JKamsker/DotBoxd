namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;

internal sealed record PluginKernelModelResult(PluginKernelModel? Model, Diagnostic? Diagnostic);

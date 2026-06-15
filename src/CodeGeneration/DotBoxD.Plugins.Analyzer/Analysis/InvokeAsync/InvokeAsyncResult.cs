using DotBoxD.Plugins.Analyzer.Analysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed record InvokeAsyncResult(
    GeneratedPluginPackage Package,
    InvokeAsyncInterception? Interception);

internal sealed record InvokeAsyncInterception(
    string AttributeSyntax,
    string ReceiverType,
    string HostAccessType,
    string ReturnType,
    string? CaptureType,
    string? CaptureDelegateType,
    string PluginId,
    string PackageFullName,
    string ArgumentsExpression,
    string ResultExpression,
    EquatableArray<string> SyncOutAssignments,
    string Helpers);

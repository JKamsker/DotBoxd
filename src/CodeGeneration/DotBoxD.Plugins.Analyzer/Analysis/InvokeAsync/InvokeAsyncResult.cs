namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed record InvokeAsyncResult(
    GeneratedPluginPackage Package,
    InvokeAsyncInterception? Interception);

internal sealed record InvokeAsyncInterception(
    string AttributeSyntax,
    string ReceiverType,
    string? ServerAccessType,
    string HostAccessType,
    string ReturnType,
    string? CaptureType,
    string? CaptureDelegateType,
    string PluginId,
    string PackageFullName,
    string ArgumentsExpression,
    string ResultExpression,
    EquatableArray<string> SyncOutAssignments,
    bool UsesReflectionCaptures,
    string Helpers);

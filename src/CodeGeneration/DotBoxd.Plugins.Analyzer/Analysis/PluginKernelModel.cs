namespace DotBoxd.Plugins.Analyzer;

internal sealed record PluginKernelModel(
    string PluginId,
    string Namespace,
    string KernelName,
    string PackageName,
    string EventName,
    string EventParameterName,
    string ContextParameterName,
    string HandleEventParameterName,
    string HandleContextParameterName,
    EquatableArray<EventPropertyModel> EventProperties,
    EquatableArray<LiveSettingModel> LiveSettings,
    DotBoxdStatementBodyModel ShouldHandle,
    DotBoxdHandleModel Handle,
    EquatableArray<string> ManifestEffects,
    EquatableArray<string> RequiredCapabilities);

internal sealed record EventPropertyModel(string Name, string Type);

internal sealed record LiveSettingModel(
    string Name,
    string Type,
    string DefaultValue,
    string? Min,
    string? Max);

namespace SafeIR.PluginAnalyzer;

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
    SafeIrExpressionModel ShouldHandle,
    SafeIrHandleModel Handle,
    EquatableArray<string> ManifestEffects);

internal sealed record EventPropertyModel(string Name, string Type);

internal sealed record LiveSettingModel(
    string Name,
    string Type,
    string DefaultValue,
    string? Min,
    string? Max);

namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;

internal sealed class SafeIrExpressionLoweringContext
{
    public SafeIrExpressionLoweringContext(
        string eventParameterName,
        EquatableArray<EventPropertyModel> eventProperties,
        EquatableArray<LiveSettingModel> liveSettings,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        EventParameterName = eventParameterName;
        EventProperties = eventProperties;
        LiveSettings = liveSettings;
        SemanticModel = semanticModel;
        CancellationToken = cancellationToken;
    }

    public string EventParameterName { get; }

    public EquatableArray<EventPropertyModel> EventProperties { get; }

    public EquatableArray<LiveSettingModel> LiveSettings { get; }

    public SemanticModel SemanticModel { get; }

    public CancellationToken CancellationToken { get; }
}

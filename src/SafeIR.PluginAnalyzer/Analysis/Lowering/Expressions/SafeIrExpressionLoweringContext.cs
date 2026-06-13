namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;

internal sealed class SafeIrExpressionLoweringContext
{
    public SafeIrExpressionLoweringContext(
        string eventParameterName,
        EquatableArray<EventPropertyModel> eventProperties,
        EquatableArray<LiveSettingModel> liveSettings,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string? projectedElementName = null,
        SafeIrExpressionModel? projectedElement = null,
        ICollection<string>? capabilities = null,
        ICollection<string>? effects = null)
    {
        EventParameterName = eventParameterName;
        EventProperties = eventProperties;
        LiveSettings = liveSettings;
        SemanticModel = semanticModel;
        CancellationToken = cancellationToken;
        ProjectedElementName = projectedElementName;
        ProjectedElement = projectedElement;
        Capabilities = capabilities;
        Effects = effects;
    }

    public string EventParameterName { get; }

    public EquatableArray<EventPropertyModel> EventProperties { get; }

    public EquatableArray<LiveSettingModel> LiveSettings { get; }

    public SemanticModel SemanticModel { get; }

    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// For a lowered hook chain after a <c>Select</c>: the name the downstream lambda gives the
    /// projected element, bound to its already-lowered IR (<see cref="ProjectedElement"/>). Null in
    /// event mode (kernels and pre-Select stages), where the element is the event itself.
    /// </summary>
    public string? ProjectedElementName { get; }

    public SafeIrExpressionModel? ProjectedElement { get; }

    /// <summary>
    /// Sink for capabilities the lowered IR requires (a <c>ctx.Messages.Send</c>, a
    /// <c>[HostBinding]</c> call, or a read of a <c>[Capability]</c>-gated event property). Collected per
    /// kernel/chain and emitted as the manifest's required capabilities. Null when a throwaway context is
    /// used for sub-expression lowering that does not contribute to the model.
    /// </summary>
    public ICollection<string>? Capabilities { get; }

    /// <summary>
    /// Sink for the sandbox effect names a <c>[HostBinding]</c> call declares (e.g. <c>HostStateRead</c>),
    /// unioned into the manifest's effects so they match the verified entrypoint effects. Null for
    /// throwaway sub-expression contexts.
    /// </summary>
    public ICollection<string>? Effects { get; }
}

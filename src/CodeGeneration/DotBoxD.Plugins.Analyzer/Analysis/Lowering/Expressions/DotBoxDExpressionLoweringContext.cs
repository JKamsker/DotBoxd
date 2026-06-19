using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal sealed class DotBoxDExpressionLoweringContext
{
    private readonly IReadOnlyCollection<string>? _inlineStack;

    public DotBoxDExpressionLoweringContext(
        string eventParameterName,
        EquatableArray<EventPropertyModel> eventProperties,
        EquatableArray<LiveSettingModel> liveSettings,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string? projectedElementName = null,
        DotBoxDExpressionModel? projectedElement = null,
        ITypeSymbol? projectedElementType = null,
        ICollection<string>? capabilities = null,
        ICollection<string>? effects = null,
        IReadOnlyDictionary<string, DotBoxDExpressionModel>? inlinedBindings = null,
        IReadOnlyCollection<string>? inlineStack = null)
    {
        EventParameterName = eventParameterName;
        EventProperties = eventProperties;
        LiveSettings = liveSettings;
        SemanticModel = semanticModel;
        CancellationToken = cancellationToken;
        ProjectedElementName = projectedElementName;
        ProjectedElement = projectedElement;
        ProjectedElementType = projectedElementType;
        Capabilities = capabilities;
        Effects = effects;
        InlinedBindings = inlinedBindings;
        _inlineStack = inlineStack;
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

    public DotBoxDExpressionModel? ProjectedElement { get; }

    /// <summary>
    /// The CLR type of <see cref="ProjectedElement"/> (the <c>Select</c> body's type). When it is a DTO record,
    /// a downstream member access <c>dto.Field</c> resolves to a <c>record.get</c> on the projected value rather
    /// than being misread as a same-named event property. Null in event mode or for a non-record projection.
    /// </summary>
    public ITypeSymbol? ProjectedElementType { get; }

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

    /// <summary>
    /// When lowering an inlined <c>[KernelMethod]</c> body: each of the method's parameter names bound to
    /// the already-lowered IR of the corresponding call-site argument. An identifier matching one of these
    /// substitutes the bound IR directly (compile-time inlining, the same mechanism as
    /// <see cref="ProjectedElement"/> generalized to N parameters). Null outside an inlined body.
    /// </summary>
    public IReadOnlyDictionary<string, DotBoxDExpressionModel>? InlinedBindings { get; }

    /// <summary>True while <paramref name="methodKey"/> is already being inlined further up the stack
    /// (used to reject recursive <c>[KernelMethod]</c> chains rather than inlining forever).</summary>
    public bool IsInlining(string methodKey)
        => _inlineStack is { } stack && stack.Contains(methodKey);

    /// <summary>
    /// Builds the sub-context used to lower an inlined <c>[KernelMethod]</c> body: switches to the body's
    /// own semantic model, exposes the parameter <paramref name="bindings"/>, and pushes
    /// <paramref name="methodKey"/> onto the inline stack. The event/live-setting scopes are dropped (a
    /// static method sees only its parameters) while the capability/effect sinks are kept so any
    /// <c>[HostBinding]</c> calls inside the body still contribute to the calling kernel's manifest.
    /// </summary>
    public DotBoxDExpressionLoweringContext ForInlinedMethod(
        SemanticModel bodySemanticModel,
        IReadOnlyDictionary<string, DotBoxDExpressionModel> bindings,
        string methodKey)
    {
        var stack = new HashSet<string>(StringComparer.Ordinal);
        if (_inlineStack is not null)
        {
            foreach (var entry in _inlineStack)
            {
                stack.Add(entry);
            }
        }

        stack.Add(methodKey);
        return new DotBoxDExpressionLoweringContext(
            eventParameterName: string.Empty,
            eventProperties: default,
            liveSettings: default,
            bodySemanticModel,
            CancellationToken,
            projectedElementName: null,
            projectedElement: null,
            capabilities: Capabilities,
            effects: Effects,
            inlinedBindings: bindings,
            inlineStack: stack);
    }
}

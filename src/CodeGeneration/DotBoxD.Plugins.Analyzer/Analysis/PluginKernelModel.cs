using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

namespace DotBoxD.Plugins.Analyzer.Analysis;

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
    DotBoxDStatementBodyModel ShouldHandle,
    DotBoxDStatementBodyModel HandleBody,
    string HandleReturnTypeSource,
    EquatableArray<string> ManifestEffects,
    EquatableArray<string> RequiredCapabilities,
    EquatableArray<IndexPredicateModel> IndexPredicates,
    bool IndexCoversPredicate)
{
    /// <summary>
    /// True for a lowered remote <c>RunLocal</c> chain: the verified IR filters and projects server-side and
    /// the host pushes only the result back to the plugin's native delegate. Default false for ordinary chains.
    /// </summary>
    public bool LocalTerminal { get; init; }

    /// <summary>The manifest type the <c>Select</c> projection returns (null for a whole-event RunLocal).</summary>
    public string? ProjectedType { get; init; }

    /// <summary>Whether the host pushes the whole event or the projected value to the plugin's delegate.</summary>
    public LocalPayloadKind LocalPayloadKind { get; init; }
}

/// <summary>
/// What a remote <c>RunLocal</c> host pushes back to the plugin: the original event (no <c>Select</c>) or the
/// value the <c>Select</c> projection produced.
/// </summary>
internal enum LocalPayloadKind
{
    Event,
    Projection
}

internal sealed record EventPropertyModel(string Name, string Type);

/// <summary>
/// One index-eligible <c>event-property &lt;op&gt; constant</c> comparison extracted from a lowered
/// <c>.Where(...)</c> chain. All fields are strings so the model stays value-equatable for incremental
/// generation; <see cref="ValueLiteral"/> is the C# literal the emitter writes for the boxed constant and
/// <see cref="Operator"/> is the <c>DotBoxD.Plugins.IndexPredicateOperator</c> member name.
/// </summary>
internal sealed record IndexPredicateModel(
    string Path,
    string Operator,
    string ValueLiteral,
    string ValueType);

internal sealed record LiveSettingModel(
    string Name,
    string Type,
    string DefaultValue,
    string? Min,
    string? Max);

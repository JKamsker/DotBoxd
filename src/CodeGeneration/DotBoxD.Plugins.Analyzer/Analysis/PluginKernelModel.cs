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

    /// <summary>
    /// The manifest type the <c>Select</c> projection returns, or <c>null</c> for a whole-event
    /// <c>RunLocal</c> (no <c>Select</c>). The runtime treats a null <see cref="ProjectedType"/> on a
    /// <see cref="LocalTerminal"/> chain as a whole-event push and a non-null one as a projection push,
    /// so the payload kind needs no separate persisted field.
    /// </summary>
    public string? ProjectedType { get; init; }

    /// <summary>
    /// The generated reflection-free <c>ReadProjected(KernelRpcValue) -&gt; TProjected</c> reader (plus its
    /// conversion helpers) for a <see cref="LocalTerminal"/> chain whose projected type is wire-eligible, or
    /// <c>null</c> when there is no decoder (not a local chain, or the type falls back to the reflective decode
    /// path). Stored as an equatable string so an <c>ITypeSymbol</c> never crosses the incremental provider
    /// boundary; the emitter appends it verbatim to the package class.
    /// </summary>
    public string? LocalDecoderSource { get; init; }
}

/// <summary>
/// A lowered event property. <see cref="Type"/> is the coarse manifest tag the expression lowerer carries for
/// reads of this property (a scalar token, or a non-scalar shape tag such as <c>guid</c>/<c>list</c>/<c>record</c>);
/// <see cref="SandboxTypeSource"/> is the full C# <c>SandboxType</c> construction emitted for the kernel
/// parameter, so non-scalar (Guid/enum/list/record) properties declare the exact sandbox type the runtime
/// convention adapter produces. Empty when the property is not marshaller-eligible (the chain fails safe).
/// </summary>
internal sealed record EventPropertyModel(string Name, string Type, string SandboxTypeSource);

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

namespace SafeIR.Plugins;

using SafeIR;

public sealed record PluginExecutionObservation(
    string Entrypoint,
    ExecutionMode RequestedMode,
    ExecutionMode ActualMode,
    bool Succeeded,
    SandboxErrorCode? ErrorCode,
    SandboxErrorCode? FallbackReason,
    string CacheStatus,
    string? RuntimeForm,
    string? CacheKey,
    string? ArtifactHash,
    string? MaterializationStatus);

internal sealed class PluginExecutionObserver
{
    private readonly object _gate = new();
    private readonly List<PluginExecutionObservation> _observations = [];
    private PluginExecutionObservation? _last;

    public PluginExecutionObservation? Last
    {
        get
        {
            lock (_gate)
            {
                return _last;
            }
        }
    }

    public IReadOnlyList<PluginExecutionObservation> Snapshot()
    {
        lock (_gate)
        {
            return _observations.ToArray();
        }
    }

    public void Record(string entrypoint, ExecutionMode requestedMode, SandboxExecutionResult result)
    {
        var summary = result.AuditEvents.LastOrDefault(e => e.Kind == "RunSummary")?.Fields;
        var fallback = result.AuditEvents.FirstOrDefault(e => e.Kind == "ExecutionFallback");
        var observation = new PluginExecutionObservation(
            entrypoint,
            requestedMode,
            result.ActualMode,
            result.Succeeded,
            result.Error?.Code,
            fallback?.ErrorCode,
            Field(summary, "cacheStatus") ?? "None",
            Field(summary, "runtimeForm"),
            Field(summary, "cacheKey"),
            result.ArtifactHash ?? Field(summary, "artifactHash"),
            Field(summary, "materializationStatus"));

        lock (_gate)
        {
            _observations.Add(observation);
            _last = observation;
        }
    }

    private static string? Field(IReadOnlyDictionary<string, string>? fields, string key)
        => fields is not null && fields.TryGetValue(key, out var value) ? value : null;
}

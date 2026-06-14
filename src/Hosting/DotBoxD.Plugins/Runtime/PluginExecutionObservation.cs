namespace DotBoxD.Plugins;

using DotBoxD.Hosting;
using DotBoxD.Kernels;

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
    // Bounded retention window. Long-running hosts can drive millions of executions
    // through a single installed kernel, so full history is kept in a fixed-size ring
    // buffer instead of an unbounded list: memory tracks active diagnostics rather than
    // lifetime event volume, and Snapshot copies only this window. LastExecution stays
    // the always-on hot-path diagnostic for the most recent run.
    private const int HistoryCapacity = 128;

    private readonly object _gate = new();
    private readonly ObservationEntry[] _observations = new ObservationEntry[HistoryCapacity];
    private int _start;
    private int _count;
    private ObservationEntry _last;
    private bool _hasLast;

    public PluginExecutionObservation? Last
    {
        get
        {
            lock (_gate)
            {
                return _hasLast ? _last.ToObservation() : null;
            }
        }
    }

    public IReadOnlyList<PluginExecutionObservation> Snapshot()
    {
        lock (_gate)
        {
            var snapshot = new PluginExecutionObservation[_count];
            for (var i = 0; i < _count; i++)
            {
                snapshot[i] = _observations[(_start + i) % HistoryCapacity].ToObservation();
            }

            return snapshot;
        }
    }

    public void Record(string entrypoint, ExecutionMode requestedMode, SandboxExecutionResult result)
    {
        if (result is { Succeeded: true, Error: null, AuditEvents.Count: 0 })
        {
            RecordNoAuditSuccess(entrypoint, requestedMode, result.ActualMode, result.ArtifactHash);
            return;
        }

        ExtractMarkers(requestedMode, result, out var summary, out var fallbackReason);
        var observation = new ObservationEntry(
            entrypoint,
            requestedMode,
            result.ActualMode,
            result.Succeeded,
            result.Error?.Code,
            fallbackReason,
            Field(summary, "cacheStatus") ?? "None",
            Field(summary, "runtimeForm"),
            Field(summary, "cacheKey"),
            result.ArtifactHash ?? Field(summary, "artifactHash"),
            Field(summary, "materializationStatus"));
        Append(observation);
    }

    public void Record(string entrypoint, ExecutionMode requestedMode, PreparedExecutionResult result)
    {
        if (result.FullResult is { } fullResult)
        {
            Record(entrypoint, requestedMode, fullResult);
            return;
        }

        RecordNoAuditSuccess(entrypoint, requestedMode, result.ActualMode, result.ArtifactHash);
    }

    private void RecordNoAuditSuccess(
        string entrypoint,
        ExecutionMode requestedMode,
        ExecutionMode actualMode,
        string? artifactHash)
        => Append(new ObservationEntry(
            entrypoint,
            requestedMode,
            actualMode,
            Succeeded: true,
            ErrorCode: null,
            FallbackReason: null,
            CacheStatus: "None",
            RuntimeForm: null,
            CacheKey: null,
            artifactHash,
            MaterializationStatus: null));

    private void Append(ObservationEntry observation)
    {
        lock (_gate)
        {
            var slot = (_start + _count) % HistoryCapacity;
            _observations[slot] = observation;
            if (_count < HistoryCapacity)
            {
                _count++;
            }
            else
            {
                _start = (_start + 1) % HistoryCapacity;
            }

            _last = observation;
            _hasLast = true;
        }
    }

    // Walks the audit-event list a single time to recover the two telemetry markers the
    // observation needs: the last RunSummary's fields and the first ExecutionFallback's
    // error code. This replaces two independent full-list LINQ scans, so extraction stays
    // a single pass over the events even as audit volume grows.
    private static void ExtractMarkers(
        ExecutionMode requestedMode,
        SandboxExecutionResult result,
        out IReadOnlyDictionary<string, string>? summary,
        out SandboxErrorCode? fallbackReason)
    {
        summary = null;
        fallbackReason = null;
        if (requestedMode == ExecutionMode.Interpreted && result.ActualMode == ExecutionMode.Interpreted)
        {
            return;
        }

        var auditEvents = result.AuditEvents;
        var fallbackFound = false;

        for (var i = 0; i < auditEvents.Count; i++)
        {
            var auditEvent = auditEvents[i];
            switch (auditEvent.Kind)
            {
                case "RunSummary":
                    summary = auditEvent.Fields;
                    break;
                case "ExecutionFallback" when !fallbackFound:
                    fallbackReason = auditEvent.ErrorCode;
                    fallbackFound = true;
                    break;
            }
        }
    }

    private static string? Field(IReadOnlyDictionary<string, string>? fields, string key)
        => fields is not null && fields.TryGetValue(key, out var value) ? value : null;

    private readonly record struct ObservationEntry(
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
        string? MaterializationStatus)
    {
        public PluginExecutionObservation ToObservation()
            => new(
                Entrypoint,
                RequestedMode,
                ActualMode,
                Succeeded,
                ErrorCode,
                FallbackReason,
                CacheStatus,
                RuntimeForm,
                CacheKey,
                ArtifactHash,
                MaterializationStatus);
    }
}

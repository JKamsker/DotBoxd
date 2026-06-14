namespace DotBoxD.Kernels;

using System.Collections.ObjectModel;

public sealed record SandboxRunId(Guid Value)
{
    public static SandboxRunId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}

public enum AuditLevel
{
    None,
    Summary,
    PerCall,
    PerResource,
    FullInputOutput
}

public sealed record SandboxAuditEvent(
    SandboxRunId RunId,
    string Kind,
    DateTimeOffset Timestamp,
    bool Success,
    string? BindingId = null,
    string? CapabilityId = null,
    SandboxEffect Effect = SandboxEffect.None,
    string? ResourceId = null,
    SandboxErrorCode? ErrorCode = null,
    string? Message = null,
    long? Bytes = null,
    IReadOnlyDictionary<string, string>? Fields = null,
    long SequenceNumber = 0)
{
    private IReadOnlyDictionary<string, string>? _fields = CopyFields(Fields);

    public IReadOnlyDictionary<string, string>? Fields { get => _fields; init => _fields = CopyFields(value); }

    private static IReadOnlyDictionary<string, string>? CopyFields(IReadOnlyDictionary<string, string>? fields)
        => fields is null ? null : ModelCopy.StringDictionary(fields);
}

internal sealed class OwnedAuditEventSnapshot(IList<SandboxAuditEvent> list)
    : ReadOnlyCollection<SandboxAuditEvent>(list);

public interface IAuditSink
{
    long EventsWritten { get; }

    void Write(SandboxAuditEvent auditEvent);

    bool HasBindingAuditSince(
        BindingDescriptor descriptor,
        long checkpoint,
        bool success,
        SandboxErrorCode? expectedErrorCode,
        SandboxRunId runId,
        string moduleHash,
        string policyHash);
}

public sealed class InMemoryAuditSink : IAuditSink
{
    private static readonly IReadOnlyList<SandboxAuditEvent> EmptySnapshot =
        new OwnedAuditEventSnapshot(Array.Empty<SandboxAuditEvent>());

    private List<SandboxAuditEvent>? _events;
    private long _sequence;

    public IReadOnlyList<SandboxAuditEvent> Events
        => _events is null ? Array.Empty<SandboxAuditEvent>() : _events.ToArray();

    public long EventsWritten => _sequence;

    /// <summary>
    /// Produces a single owned, immutable snapshot of the recorded events.
    /// The returned <see cref="ReadOnlyCollection{T}"/> wraps a fresh array that is not
    /// retained by the sink, so result construction can adopt it without copying again.
    /// </summary>
    internal IReadOnlyList<SandboxAuditEvent> SnapshotEvents()
        => _events is null || _events.Count == 0 ? EmptySnapshot : new OwnedAuditEventSnapshot(_events.ToArray());

    public void Write(SandboxAuditEvent auditEvent)
    {
        var sequence = ++_sequence;
        _events ??= new List<SandboxAuditEvent>();
        _events.Add(auditEvent with { SequenceNumber = sequence });
    }

    public bool HasBindingAuditSince(
        BindingDescriptor descriptor,
        long checkpoint,
        bool success,
        SandboxErrorCode? expectedErrorCode,
        SandboxRunId runId,
        string moduleHash,
        string policyHash)
    {
        // Sequence numbers are assigned monotonically on append (Write sets
        // SequenceNumber = ++_sequence) and _events is never reordered or
        // pruned, so _events[i].SequenceNumber == i + 1. A checkpoint is the
        // sequence count recorded before the current binding call, which means
        // the first event with SequenceNumber > checkpoint lives at list index
        // checkpoint. Start enumeration there instead of rescanning prior
        // events, avoiding O(N^2) enforcement work over a run.
        var events = _events;
        if (events is null)
        {
            return false;
        }

        for (var index = StartIndexAfter(checkpoint, events.Count); index < events.Count; index++)
        {
            var e = events[index];
            if (e.RunId == runId &&
                e.Success == success &&
                IsBindingAuditKind(e.Kind) &&
                StringComparer.Ordinal.Equals(e.BindingId, descriptor.Id) &&
                CapabilityMatches(e, descriptor) &&
                EffectMatches(e, descriptor) &&
                !string.IsNullOrWhiteSpace(e.ResourceId) &&
                HasRequiredFields(e, moduleHash, policyHash) &&
                ResultMatches(e, success, expectedErrorCode))
            {
                return true;
            }
        }

        return false;
    }

    private static int StartIndexAfter(long checkpoint, int eventCount)
    {
        // Fail closed against an out-of-range checkpoint: a negative checkpoint
        // means "scan all events", and one beyond the recorded count yields an
        // empty range. The bounded clamp keeps the index a valid list offset.
        if (checkpoint <= 0)
        {
            return 0;
        }

        return checkpoint >= eventCount ? eventCount : (int)checkpoint;
    }

    private static bool IsBindingAuditKind(string kind)
        => kind is "BindingCall" or "SandboxLog" or "PluginMessage";

    private static bool CapabilityMatches(SandboxAuditEvent auditEvent, BindingDescriptor descriptor)
        => descriptor.RequiredCapability is null ||
           StringComparer.Ordinal.Equals(auditEvent.CapabilityId, descriptor.RequiredCapability);

    private static bool EffectMatches(SandboxAuditEvent auditEvent, BindingDescriptor descriptor)
    {
        if (auditEvent.Effect == SandboxEffect.None ||
            (auditEvent.Effect & ~descriptor.Effects) != SandboxEffect.None)
        {
            return false;
        }

        var nonCpuEffects = descriptor.Effects & ~SandboxEffect.Cpu;
        return nonCpuEffects == SandboxEffect.None ||
               (auditEvent.Effect & nonCpuEffects) != SandboxEffect.None;
    }

    private static bool ResultMatches(SandboxAuditEvent auditEvent, bool success, SandboxErrorCode? expectedErrorCode)
        => success
            ? auditEvent.ErrorCode is null
            : auditEvent.ErrorCode is not null && auditEvent.ErrorCode == expectedErrorCode;

    private static bool HasRequiredFields(SandboxAuditEvent auditEvent, string moduleHash, string policyHash)
    {
        if (auditEvent.Fields is null ||
            !auditEvent.Fields.TryGetValue("resourceKind", out var resourceKind) ||
            string.IsNullOrWhiteSpace(resourceKind) ||
            !auditEvent.Fields.TryGetValue("durationMs", out var durationMs) ||
            !auditEvent.Fields.TryGetValue("moduleHash", out var auditModuleHash) ||
            !StringComparer.Ordinal.Equals(auditModuleHash, moduleHash) ||
            !auditEvent.Fields.TryGetValue("policyHash", out var auditPolicyHash) ||
            !StringComparer.Ordinal.Equals(auditPolicyHash, policyHash))
        {
            return false;
        }

        return double.TryParse(
                durationMs,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) &&
            parsed >= 0;
    }
}

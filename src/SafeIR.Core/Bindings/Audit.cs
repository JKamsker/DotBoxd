namespace SafeIR;

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
    private readonly List<SandboxAuditEvent> _events = [];
    private long _sequence;

    public IReadOnlyList<SandboxAuditEvent> Events => _events.ToArray();

    public long EventsWritten => _sequence;

    /// <summary>
    /// Produces a single owned, immutable snapshot of the recorded events.
    /// The returned <see cref="ReadOnlyCollection{T}"/> wraps a fresh array that is not
    /// retained by the sink, so result construction can adopt it without copying again.
    /// </summary>
    internal IReadOnlyList<SandboxAuditEvent> SnapshotEvents()
        => new ReadOnlyCollection<SandboxAuditEvent>(_events.ToArray());

    public void Write(SandboxAuditEvent auditEvent)
    {
        var sequence = ++_sequence;
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
        => _events.Any(e =>
            e.SequenceNumber > checkpoint &&
            e.RunId == runId &&
            e.Success == success &&
            IsBindingAuditKind(e.Kind) &&
            StringComparer.Ordinal.Equals(e.BindingId, descriptor.Id) &&
            CapabilityMatches(e, descriptor) &&
            EffectMatches(e, descriptor) &&
            !string.IsNullOrWhiteSpace(e.ResourceId) &&
            HasRequiredFields(e, moduleHash, policyHash) &&
            ResultMatches(e, success, expectedErrorCode));

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

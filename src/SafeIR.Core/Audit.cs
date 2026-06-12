namespace SafeIR;

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
    long SequenceNumber = 0);

public interface IAuditSink
{
    long EventsWritten { get; }

    void Write(SandboxAuditEvent auditEvent);

    bool HasBindingAuditSince(string bindingId, long checkpoint, bool success);
}

public sealed class InMemoryAuditSink : IAuditSink
{
    private readonly List<SandboxAuditEvent> _events = [];
    private long _sequence;

    public IReadOnlyList<SandboxAuditEvent> Events => _events;

    public long EventsWritten => _sequence;

    public void Write(SandboxAuditEvent auditEvent)
    {
        var sequence = ++_sequence;
        _events.Add(auditEvent with { SequenceNumber = sequence });
    }

    public bool HasBindingAuditSince(string bindingId, long checkpoint, bool success)
        => _events.Any(e =>
            e.SequenceNumber > checkpoint &&
            e.Success == success &&
            e.Kind != "DebugTrace" &&
            StringComparer.Ordinal.Equals(e.BindingId, bindingId));
}

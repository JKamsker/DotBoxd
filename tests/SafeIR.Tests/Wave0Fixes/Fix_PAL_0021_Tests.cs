using System.Collections.ObjectModel;

using SafeIR.Hosting;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for PAL-0021: execution result audit events were copied multiple
/// times. The sink now hands result construction a single owned, immutable snapshot, and the
/// <see cref="SandboxExecutionResult.AuditEvents"/> setter adopts that snapshot without
/// allocating another copy, while still defending against mutable inputs (COR-0014).
/// </summary>
public sealed class Fix_PAL_0021_Tests
{
    private static SandboxResourceUsage Usage()
        => new ResourceMeter(new ResourceLimits(MaxFuel: 1_000)).Snapshot();

    private static SandboxExecutionResult ResultWith(IReadOnlyList<SandboxAuditEvent> auditEvents)
        => new()
        {
            Succeeded = true,
            Value = SandboxValue.Unit,
            ResourceUsage = Usage(),
            AuditEvents = auditEvents,
            ActualMode = ExecutionMode.Interpreted,
            ExecutionDispatched = true,
            ModuleHash = "module",
            PlanHash = "plan",
            PolicyHash = "policy"
        };

    [Fact]
    public void Sink_snapshot_is_an_owned_read_only_collection()
    {
        var sink = new InMemoryAuditSink();
        sink.Write(new SandboxAuditEvent(SandboxRunId.New(), "RunSummary", DateTimeOffset.UtcNow, true));

        var snapshot = sink.SnapshotEvents();

        Assert.IsAssignableFrom<ReadOnlyCollection<SandboxAuditEvent>>(snapshot);
        Assert.Single(snapshot);
    }

    [Fact]
    public void Result_adopts_owned_sink_snapshot_without_copying_again()
    {
        var sink = new InMemoryAuditSink();
        sink.Write(new SandboxAuditEvent(SandboxRunId.New(), "RunSummary", DateTimeOffset.UtcNow, true));
        var snapshot = sink.SnapshotEvents();

        var result = ResultWith(snapshot);

        // No second O(event-count) copy: the result holds the exact owned snapshot instance.
        Assert.Same(snapshot, result.AuditEvents);
    }

    [Fact]
    public void Hosting_owned_snapshot_is_adopted_without_copying_again()
    {
        var sink = new InMemoryAuditSink();
        sink.Write(new SandboxAuditEvent(SandboxRunId.New(), "RunSummary", DateTimeOffset.UtcNow, true));
        var snapshot = sink.OwnedEventSnapshot();

        var result = ResultWith(snapshot);

        Assert.IsAssignableFrom<ReadOnlyCollection<SandboxAuditEvent>>(snapshot);
        Assert.Same(snapshot, result.AuditEvents);
    }

    [Fact]
    public void Mutable_list_input_is_still_defensively_copied()
    {
        var events = new List<SandboxAuditEvent>
        {
            new(SandboxRunId.New(), "RunSummary", DateTimeOffset.UtcNow, true)
        };

        var result = ResultWith(events);
        events.Clear();

        // A mutable input must never alias into the public result (COR-0014).
        Assert.NotSame(events, result.AuditEvents);
        Assert.Single(result.AuditEvents);
    }

    [Fact]
    public void Public_read_only_collection_input_is_still_defensively_copied()
    {
        var events = new List<SandboxAuditEvent>
        {
            new(SandboxRunId.New(), "RunSummary", DateTimeOffset.UtcNow, true)
        };
        var wrapper = new ReadOnlyCollection<SandboxAuditEvent>(events);

        var result = ResultWith(wrapper);
        events.Clear();

        Assert.NotSame(wrapper, result.AuditEvents);
        Assert.Single(result.AuditEvents);
    }

    [Fact]
    public void Resequencing_returns_owned_snapshot_adopted_without_copying_again()
    {
        var runId = SandboxRunId.New();
        var events = new[]
        {
            new SandboxAuditEvent(runId, "First", DateTimeOffset.UtcNow, true),
            new SandboxAuditEvent(runId, "Second", DateTimeOffset.UtcNow, true)
        };

        var sequenced = events.ToSequencedArray();
        var result = ResultWith(sequenced);

        Assert.IsAssignableFrom<ReadOnlyCollection<SandboxAuditEvent>>(sequenced);
        Assert.Same(sequenced, result.AuditEvents);
        Assert.Equal(2, result.AuditEvents.Count);
        Assert.Equal(1, result.AuditEvents[0].SequenceNumber);
        Assert.Equal(2, result.AuditEvents[1].SequenceNumber);
    }
}

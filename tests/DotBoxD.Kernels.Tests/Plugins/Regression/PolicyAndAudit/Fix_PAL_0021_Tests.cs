using System.Collections.ObjectModel;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Plugins.Regression.PolicyAndAudit;

/// <summary>
/// Regression coverage for PAL-0021: execution result audit events were copied multiple
/// times. The sink now hands result construction a single owned, immutable snapshot, and the
/// <see cref="SandboxExecutionResult.AuditEvents"/> setter adopts that snapshot without
/// allocating another copy, while still defending against mutable inputs (COR-0014).
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class Fix_PAL_0021_Tests
{
    private const int EmptySinkWarmupIterations = 1_000;
    private const int EmptySinkMeasuredIterations = 20_000;

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
    public void Empty_sink_construction_does_not_allocate_an_event_list()
    {
        for (var i = 0; i < EmptySinkWarmupIterations; i++)
        {
            GC.KeepAlive(new InMemoryAuditSink().SnapshotEvents());
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < EmptySinkMeasuredIterations; i++)
        {
            GC.KeepAlive(new InMemoryAuditSink().SnapshotEvents());
        }

        var perSink = (GC.GetAllocatedBytesForCurrentThread() - before) / EmptySinkMeasuredIterations;

        Assert.True(
            perSink < 56,
            $"Empty audit sink construction allocated {perSink} bytes/sink; the event list should be created lazily.");
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

    [Fact]
    public void Already_sequenced_owned_snapshot_is_reused()
    {
        var sink = new InMemoryAuditSink();
        sink.Write(new SandboxAuditEvent(SandboxRunId.New(), "First", DateTimeOffset.UtcNow, true));
        sink.Write(new SandboxAuditEvent(SandboxRunId.New(), "Second", DateTimeOffset.UtcNow, true));
        var snapshot = sink.OwnedEventSnapshot();

        var sequenced = snapshot.ToSequencedArray();
        var result = ResultWith(sequenced);

        Assert.Same(snapshot, sequenced);
        Assert.Same(sequenced, result.AuditEvents);
    }

    [Fact]
    public void Already_sequenced_public_list_is_wrapped_as_owned_snapshot()
    {
        var runId = SandboxRunId.New();
        var events = new[]
        {
            new SandboxAuditEvent(runId, "First", DateTimeOffset.UtcNow, true, SequenceNumber: 1),
            new SandboxAuditEvent(runId, "Second", DateTimeOffset.UtcNow, true, SequenceNumber: 2)
        };

        var sequenced = events.ToSequencedArray();
        var result = ResultWith(sequenced);

        Assert.NotSame(events, sequenced);
        Assert.IsAssignableFrom<ReadOnlyCollection<SandboxAuditEvent>>(sequenced);
        Assert.Same(sequenced, result.AuditEvents);
        Assert.Equal(1, result.AuditEvents[0].SequenceNumber);
        Assert.Equal(2, result.AuditEvents[1].SequenceNumber);
    }
}

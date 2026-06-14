using SafeIR.Runtime;

namespace SafeIR.Tests;

/// <summary>
/// ALG-0011: required audit enforcement must inspect only events written after the
/// caller's checkpoint instead of rescanning the whole audit log on every binding
/// call. These tests pin the observable behavior of <see cref="InMemoryAuditSink.HasBindingAuditSince"/>
/// so the checkpoint-offset optimization cannot change which events satisfy a check.
/// </summary>
public sealed class Fix_ALG_0011_Tests
{
    private static readonly SandboxRunId Run = SandboxRunId.New();
    private const string ModuleHash = "module-hash";
    private const string PolicyHash = "policy-hash";

    [Fact]
    public void Matching_event_after_checkpoint_satisfies_required_audit()
    {
        var sink = new InMemoryAuditSink();
        var descriptor = AuditedDescriptor();

        var checkpoint = sink.EventsWritten;
        sink.Write(MatchingEvent());

        Assert.True(HasMatch(sink, descriptor, checkpoint));
    }

    [Fact]
    public void Matching_event_at_or_before_checkpoint_is_ignored()
    {
        var sink = new InMemoryAuditSink();
        var descriptor = AuditedDescriptor();

        // A valid event recorded before the current binding's checkpoint belongs to a
        // prior call and must not satisfy this call's required-audit requirement.
        sink.Write(MatchingEvent());
        var checkpoint = sink.EventsWritten;

        Assert.False(HasMatch(sink, descriptor, checkpoint));
    }

    [Fact]
    public void Post_checkpoint_match_is_found_after_many_prior_events()
    {
        var sink = new InMemoryAuditSink();
        var descriptor = AuditedDescriptor();

        // Starting enumeration at the checkpoint offset must not skip a valid event that
        // sits well beyond a large block of unrelated prior events.
        for (var i = 0; i < 5_000; i++)
        {
            sink.Write(UnrelatedEvent());
        }

        var checkpoint = sink.EventsWritten;
        sink.Write(UnrelatedEvent());
        sink.Write(MatchingEvent());

        Assert.True(HasMatch(sink, descriptor, checkpoint));
    }

    [Fact]
    public void Result_is_independent_of_prior_matching_events()
    {
        var withHistory = new InMemoryAuditSink();
        var withoutHistory = new InMemoryAuditSink();
        var descriptor = AuditedDescriptor();

        for (var i = 0; i < 1_000; i++)
        {
            withHistory.Write(MatchingEvent());
        }

        var historyCheckpoint = withHistory.EventsWritten;
        var freshCheckpoint = withoutHistory.EventsWritten;

        // No post-checkpoint match exists in either sink, so accumulated history that
        // only lives before the checkpoint must not change the negative result.
        Assert.Equal(
            HasMatch(withoutHistory, descriptor, freshCheckpoint),
            HasMatch(withHistory, descriptor, historyCheckpoint));
        Assert.False(HasMatch(withHistory, descriptor, historyCheckpoint));
    }

    [Fact]
    public void Negative_checkpoint_scans_all_events()
    {
        var sink = new InMemoryAuditSink();
        var descriptor = AuditedDescriptor();

        // A checkpoint at or below the first sequence must fail closed by scanning every
        // recorded event rather than skipping the head of the list.
        sink.Write(MatchingEvent());

        Assert.True(HasMatch(sink, descriptor, checkpoint: -1));
        Assert.True(HasMatch(sink, descriptor, checkpoint: 0));
    }

    [Fact]
    public void Checkpoint_beyond_recorded_events_matches_nothing()
    {
        var sink = new InMemoryAuditSink();
        var descriptor = AuditedDescriptor();

        sink.Write(MatchingEvent());

        Assert.False(HasMatch(sink, descriptor, checkpoint: sink.EventsWritten + 10));
    }

    private static bool HasMatch(InMemoryAuditSink sink, BindingDescriptor descriptor, long checkpoint)
        => sink.HasBindingAuditSince(
            descriptor,
            checkpoint,
            success: true,
            expectedErrorCode: null,
            Run,
            ModuleHash,
            PolicyHash);

    private static SandboxAuditEvent MatchingEvent()
        => new(
            Run,
            "BindingCall",
            DateTimeOffset.UtcNow,
            Success: true,
            BindingId: "test.audited",
            Effect: SandboxEffect.Cpu,
            ResourceId: "binding:test.audited",
            ErrorCode: null,
            Fields: BindingAuditFields.Create("binding", DateTimeOffset.UtcNow, ModuleHash, PolicyHash, deterministic: true));

    private static SandboxAuditEvent UnrelatedEvent()
        => new(
            Run,
            "PolicyDenied",
            DateTimeOffset.UtcNow,
            Success: false,
            CapabilityId: "test.cap",
            ResourceId: "capability:test.cap",
            ErrorCode: SandboxErrorCode.PermissionDenied);

    private static BindingDescriptor AuditedDescriptor()
        => new(
            "test.audited",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.FromInt32(0)),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
}

namespace SafeIR;

public sealed class SandboxContext
{
    private Random? _random;

    public SandboxContext(
        SandboxRunId runId,
        SandboxPolicy policy,
        ResourceMeter budget,
        BindingRegistry bindings,
        IAuditSink audit,
        CancellationToken cancellationToken)
    {
        RunId = runId;
        Policy = policy;
        Budget = budget;
        Bindings = bindings;
        Audit = audit;
        CancellationToken = cancellationToken;
    }

    public SandboxRunId RunId { get; }
    public SandboxPolicy Policy { get; }
    public ResourceMeter Budget { get; }
    public BindingRegistry Bindings { get; }
    public IAuditSink Audit { get; }
    public CancellationToken CancellationToken { get; }

    public void RequireCapability(string capabilityId)
    {
        if (!Policy.GrantsCapability(capabilityId)) {
            Audit.Write(new SandboxAuditEvent(
                RunId,
                "PolicyDenied",
                DateTimeOffset.UtcNow,
                Success: false,
                CapabilityId: capabilityId,
                ErrorCode: SandboxErrorCode.PermissionDenied,
                Message: $"capability {capabilityId} denied"));
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.PermissionDenied,
                $"capability {capabilityId} is not granted"));
        }
    }

    public CapabilityGrant GetCapability(string capabilityId)
    {
        RequireCapability(capabilityId);
        return Policy.GetGrant(capabilityId);
    }

    public void ChargeFuel(long amount)
    {
        CancellationToken.ThrowIfCancellationRequested();
        Budget.ChargeFuel(amount);
    }

    public void ChargeAllocation(long bytes) => Budget.ChargeAllocation(bytes);

    public DateTimeOffset UtcNow()
    {
        if (Policy.Deterministic) {
            return Policy.LogicalNow ?? throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.PolicyDenied,
                "deterministic time requires a logical clock"));
        }

        return DateTimeOffset.UtcNow;
    }

    public int NextRandomInt32(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive) {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.InvalidInput,
                "random range is invalid"));
        }

        if (Policy.Deterministic) {
            if (Policy.RandomSeed is null) {
                throw new SandboxRuntimeException(new SandboxError(
                    SandboxErrorCode.PolicyDenied,
                    "deterministic random requires a seed"));
            }

            _random ??= new Random(unchecked((int)Policy.RandomSeed.Value));
            return _random.Next(minInclusive, maxExclusive);
        }

        return Random.Shared.Next(minInclusive, maxExclusive);
    }
}

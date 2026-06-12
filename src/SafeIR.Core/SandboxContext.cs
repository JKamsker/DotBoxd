namespace SafeIR;

public sealed class SandboxContext
{
    private DeterministicRandom? _deterministicRandom;
    private int _callDepth;
    private readonly BindingReturnCreditTracker _returnCredits = new();

    public SandboxContext(
        SandboxRunId runId,
        SandboxPolicy policy,
        ResourceMeter budget,
        BindingRegistry bindings,
        IAuditSink audit,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? allowedBindingIds = null)
    {
        RunId = runId;
        Policy = policy;
        Budget = budget;
        Bindings = bindings;
        Audit = audit;
        CancellationToken = cancellationToken;
        AllowedBindingIds = allowedBindingIds;
    }

    public SandboxRunId RunId { get; }
    public SandboxPolicy Policy { get; }
    public ResourceMeter Budget { get; }
    public BindingRegistry Bindings { get; }
    public IAuditSink Audit { get; }
    public CancellationToken CancellationToken { get; }
    private IReadOnlySet<string>? AllowedBindingIds { get; }

    public void RequireCapability(string capabilityId)
    {
        if (!Policy.GrantsCapability(capabilityId))
        {
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

    public void ChargeLoopIteration(long fuelAmount)
    {
        CancellationToken.ThrowIfCancellationRequested();
        Budget.ChargeLoopIteration(fuelAmount);
    }

    public void EnterCall()
    {
        if (++_callDepth > Budget.Limits.MaxCallDepth)
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.QuotaExceeded, "call depth exceeded"));
        }
    }

    public void ExitCall()
    {
        if (_callDepth > 0)
        {
            _callDepth--;
        }
    }

    public void ChargeAllocation(long bytes)
    {
        CancellationToken.ThrowIfCancellationRequested();
        Budget.ChargeAllocation(bytes);
    }

    public void ChargeCollection(SandboxValue value) => Budget.ChargeCollection(value, CancellationToken);

    public void ChargeValue(SandboxValue value) => Budget.ChargeValue(value, CancellationToken);

    public void ChargeString(string value)
    {
        CancellationToken.ThrowIfCancellationRequested();
        Budget.ChargeString(value);
        _returnCredits.RecordString(value);
    }

    public void ChargeStringAllocation(int charLength)
    {
        CancellationToken.ThrowIfCancellationRequested();
        Budget.ChargeStringAllocation(charLength);
    }

    public void RecordStringReturnCredit(string value)
        => _returnCredits.RecordString(value);

    public IDisposable BeginBindingReturnCreditScope() => _returnCredits.BeginScope();

    public void ChargeLogEvent(string message) => Budget.ChargeLogEvent(message);

    public long AuditCheckpoint() => Audit.EventsWritten;

    public void EnsureRequiredBindingSuccessAudit(BindingDescriptor descriptor, long checkpoint)
    {
        if (descriptor.AuditLevel == AuditLevel.None ||
            Audit.HasBindingAuditSince(descriptor, checkpoint, success: true))
        {
            return;
        }

        throw new SandboxRuntimeException(new SandboxError(
            SandboxErrorCode.BindingFailure,
            $"binding '{descriptor.Id}' did not emit a required audit event"));
    }

    public void EnsureRequiredBindingFailureAudit(
        BindingDescriptor descriptor,
        long checkpoint,
        SandboxErrorCode errorCode)
    {
        if (descriptor.AuditLevel == AuditLevel.None ||
            Audit.HasBindingAuditSince(descriptor, checkpoint, success: false))
        {
            return;
        }

        var timestamp = DateTimeOffset.UtcNow;
        Audit.Write(new SandboxAuditEvent(
            RunId,
            "BindingCall",
            timestamp,
            Success: false,
            BindingId: descriptor.Id,
            CapabilityId: descriptor.RequiredCapability,
            Effect: descriptor.Effects,
            ResourceId: $"binding:{descriptor.Id}",
            ErrorCode: errorCode,
            Message: "binding failed before emitting audit",
            Fields: BindingAuditFields.Create("binding", timestamp)));
    }

    public void ChargeBindingCall(BindingDescriptor descriptor)
    {
        if (AllowedBindingIds is not null && !AllowedBindingIds.Contains(descriptor.Id))
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.ValidationError,
                $"binding '{descriptor.Id}' is not referenced by the verified execution plan"));
        }

        if (descriptor.RequiredCapability is not null)
        {
            RequireCapability(descriptor.RequiredCapability);
        }

        Budget.ChargeHostCall(descriptor.Id, descriptor.CostModel.MaxCallsPerRun);
        ChargeFuel(descriptor.CostModel.BaseFuel);
    }

    public SandboxValue ChargeBindingReturn(BindingDescriptor descriptor, SandboxValue value)
    {
        SandboxValueValidator.RequireType(
            value,
            descriptor.ReturnType,
            SandboxErrorCode.BindingFailure,
            $"binding '{descriptor.Id}' returned an unexpected value type");

        var bytes = BindingReturnCost.MeasureBytes(value);
        if (!_returnCredits.TryConsume(value))
        {
            ChargeValue(value);
        }

        if (bytes > 0 && descriptor.CostModel.PerByteFuel > 0)
        {
            ChargeFuel(CheckedFuel(bytes, descriptor.CostModel.PerByteFuel));
        }

        return value;
    }

    public CancellationTokenSource CreateWallTimeToken()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        timeout.CancelAfter(Budget.RemainingWallTime());
        return timeout;
    }

    private static long CheckedFuel(long bytes, long perByteFuel)
    {
        try
        {
            return checked(bytes * perByteFuel);
        }
        catch (OverflowException)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.QuotaExceeded,
                "binding return fuel budget exhausted"));
        }
    }

    public DateTimeOffset UtcNow()
    {
        if (Policy.Deterministic)
        {
            return Policy.LogicalNow ?? throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.PolicyDenied,
                "deterministic time requires a logical clock"));
        }

        return DateTimeOffset.UtcNow;
    }

    public int NextRandomInt32(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.InvalidInput,
                "random range is invalid"));
        }

        if (Policy.Deterministic)
        {
            if (Policy.RandomSeed is null)
            {
                throw new SandboxRuntimeException(new SandboxError(
                    SandboxErrorCode.PolicyDenied,
                    "deterministic random requires a seed"));
            }

            _deterministicRandom ??= new DeterministicRandom(Policy.RandomSeed.Value);
            return _deterministicRandom.Next(minInclusive, maxExclusive);
        }

        return Random.Shared.Next(minInclusive, maxExclusive);
    }

    private sealed class DeterministicRandom(ulong seed)
    {
        private ulong _state = seed;

        public int Next(int minInclusive, int maxExclusive)
        {
            var range = (ulong)((long)maxExclusive - minInclusive);
            var threshold = (1UL << 32) % range;
            while (true)
            {
                var value = NextUInt32();
                if (value >= threshold)
                {
                    return checked((int)(minInclusive + (long)(value % range)));
                }
            }
        }

        private uint NextUInt32()
        {
            _state += 0x9E3779B97F4A7C15UL;
            var value = _state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return (uint)(value >> 32);
        }
    }
}

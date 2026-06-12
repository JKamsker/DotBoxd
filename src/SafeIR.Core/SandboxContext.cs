namespace SafeIR;

public sealed class SandboxContext
{
    private Random? _random;
    private int _callDepth;

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

    public void ChargeAllocation(long bytes) => Budget.ChargeAllocation(bytes);

    public void ChargeCollection(SandboxValue value) => Budget.ChargeCollection(value);

    public void ChargeValue(SandboxValue value) => Budget.ChargeValue(value);

    public void ChargeString(string value) => Budget.ChargeString(value);

    public void ChargeLogEvent(string message) => Budget.ChargeLogEvent(message);

    public long AuditCheckpoint() => Audit.EventsWritten;

    public void EnsureRequiredBindingSuccessAudit(BindingDescriptor descriptor, long checkpoint)
    {
        if (descriptor.AuditLevel == AuditLevel.None ||
            Audit.HasBindingAuditSince(descriptor.Id, checkpoint, success: true))
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
            Audit.HasBindingAuditSince(descriptor.Id, checkpoint, success: false))
        {
            return;
        }

        Audit.Write(new SandboxAuditEvent(
            RunId,
            "BindingCall",
            DateTimeOffset.UtcNow,
            Success: false,
            BindingId: descriptor.Id,
            CapabilityId: descriptor.RequiredCapability,
            Effect: descriptor.Effects,
            ResourceId: $"binding:{descriptor.Id}",
            ErrorCode: errorCode,
            Message: "binding failed before emitting audit"));
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
        ChargeValue(value);

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

            _random ??= new Random(unchecked((int)Policy.RandomSeed.Value));
            return _random.Next(minInclusive, maxExclusive);
        }

        return Random.Shared.Next(minInclusive, maxExclusive);
    }
}

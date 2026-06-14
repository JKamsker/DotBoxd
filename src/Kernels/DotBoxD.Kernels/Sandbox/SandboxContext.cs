namespace DotBoxD.Kernels;

using System.Runtime.CompilerServices;

public sealed partial class SandboxContext
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
        IReadOnlySet<string>? allowedBindingIds = null,
        string? moduleHash = null,
        string? policyHash = null)
    {
        RunId = runId;
        Policy = policy;
        Budget = budget;
        Bindings = bindings;
        Audit = audit;
        CancellationToken = cancellationToken;
        AllowedBindingIds = allowedBindingIds;
        ModuleHash = moduleHash ?? "";
        PolicyHash = policyHash ?? "";
    }

    public SandboxRunId RunId { get; }
    public SandboxPolicy Policy { get; }
    public ResourceMeter Budget { get; }
    public BindingRegistry Bindings { get; }
    public IAuditSink Audit { get; }
    public CancellationToken CancellationToken { get; }
    public string ModuleHash { get; }
    public string PolicyHash { get; }
    private IReadOnlySet<string>? AllowedBindingIds { get; }

    public void RequireCapability(string capabilityId)
    {
        if (!Policy.GrantsCapability(capabilityId))
        {
            throw DenyCapability(capabilityId);
        }
    }

    public CapabilityGrant GetCapability(string capabilityId)
    {
        // Single indexed lookup: resolve the grant once and reuse it for the
        // permission decision instead of scanning the grant list twice (once to
        // authorize, once to fetch). The denial audit and error stay identical.
        return Policy.TryGetGrant(capabilityId, out var grant)
            ? grant
            : throw DenyCapability(capabilityId);
    }

    private SandboxRuntimeException DenyCapability(string capabilityId)
    {
        Audit.Write(new SandboxAuditEvent(
            RunId,
            "PolicyDenied",
            AuditTimestamp(),
            Success: false,
            CapabilityId: capabilityId,
            ResourceId: $"capability:{capabilityId}",
            ErrorCode: SandboxErrorCode.PermissionDenied,
            Message: $"capability {capabilityId} denied"));
        return new SandboxRuntimeException(new SandboxError(
            SandboxErrorCode.PermissionDenied,
            $"capability {capabilityId} is not granted"));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ChargeFuel(long amount)
    {
        CancellationToken.ThrowIfCancellationRequested();
        Budget.ChargeFuel(amount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ChargeLoopIteration(long fuelAmount)
    {
        CancellationToken.ThrowIfCancellationRequested();
        Budget.ChargeLoopIteration(fuelAmount);
    }

    public void ChargeLoopIterations(long iterations, long fuelPerIteration)
    {
        CancellationToken.ThrowIfCancellationRequested();
        Budget.ChargeLoopIterations(iterations, fuelPerIteration);
    }

    internal bool CanBulkChargeLoopIterations(long iterations, long fuelPerIteration)
        => Budget.CanChargeLoopIterations(iterations, fuelPerIteration);

    public void Checkpoint()
    {
        CancellationToken.ThrowIfCancellationRequested();
        Budget.CheckDeadline();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnterCall()
    {
        if (++_callDepth > Budget.Limits.MaxCallDepth)
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.QuotaExceeded, "call depth exceeded"));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    /// <summary>
    /// Charges a precomputed value shape and the scan-fuel its metering walk would have cost
    /// (<c>nodes / 64</c>), instead of re-walking the value. The charged fuel and shape are identical to
    /// <see cref="ChargeValue"/>; this is used by incremental collection operations (see
    /// <see cref="ValueShapeCache"/>) to avoid an O(n) re-walk on every add/set.
    /// </summary>
    internal void ChargeComposedValue(in ShapeInfo info)
    {
        CancellationToken.ThrowIfCancellationRequested();
        var scanFuel = info.Nodes / 64;
        if (scanFuel > 0)
        {
            Budget.ChargeFuel(scanFuel);
        }

        Budget.ChargeValueShape(info.Shape);
    }

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

    public string CreateChargedStringConcat(string left, string right)
    {
        var length = CheckedCharLength(left.Length, right.Length);
        ChargeStringAllocation(length);
        var text = string.Concat(left, right);
        _returnCredits.RecordString(text);
        return text;
    }

    public string CreateChargedSubstring(string value, int startIndex, int length)
    {
        if (startIndex < 0 || length < 0 || startIndex > value.Length - length)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.InvalidInput,
                "string substring range is invalid"));
        }

        ChargeStringAllocation(length);
        var text = value.Substring(startIndex, length);
        _returnCredits.RecordString(text);
        return text;
    }

    internal void RecordStringReturnCredit(string value)
        => _returnCredits.RecordString(value);

    public IDisposable BeginBindingReturnCreditScope() => _returnCredits.BeginScope();

    public void ChargeLogEvent(string message) => Budget.ChargeLogEvent(message);

    public long AuditCheckpoint() => Audit.EventsWritten;

    public void EnsureRequiredBindingSuccessAudit(BindingDescriptor descriptor, long checkpoint)
    {
        if (descriptor.AuditLevel == AuditLevel.None ||
            Audit.HasBindingAuditSince(descriptor, checkpoint, success: true, null, RunId, ModuleHash, PolicyHash))
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
            Audit.HasBindingAuditSince(descriptor, checkpoint, success: false, errorCode, RunId, ModuleHash, PolicyHash))
        {
            return;
        }

        var timestamp = AuditTimestamp();
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
            Fields: BindingAuditFields("binding", timestamp)));
    }

    public IReadOnlyDictionary<string, string> BindingAuditFields(
        string resourceKind,
        DateTimeOffset startedAt,
        long? bytesRead = null,
        long? bytesWritten = null)
        => DotBoxD.Kernels.BindingAuditFields.Create(
            resourceKind,
            startedAt,
            ModuleHash,
            PolicyHash,
            Policy.Deterministic,
            bytesRead,
            bytesWritten);

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
        var shape = SandboxValidatedValueShapeMeter.Measure(
            value,
            descriptor.ReturnType,
            SandboxErrorCode.BindingFailure,
            $"binding '{descriptor.Id}' returned an unexpected value type",
            Budget.Limits,
            CancellationToken,
            Budget);

        if (!_returnCredits.TryConsume(value))
        {
            Budget.ChargeValueShape(shape);
        }

        if (shape.StringBytes > 0 && descriptor.CostModel.PerByteFuel > 0)
        {
            ChargeFuel(CheckedFuel(shape.StringBytes, descriptor.CostModel.PerByteFuel));
        }

        return value;
    }

    private SharedWallTimeTokenSource? _sharedWallTimeToken;

    public CancellationTokenSource CreateWallTimeToken()
    {
        // Fast path: when the run token cannot be canceled there is no
        // asynchronous run cancellation to link, so reuse one wall-time source
        // for the whole run instead of allocating a linked source plus a fresh
        // CancelAfter timer per binding call. The deadline is otherwise tracked
        // by the ResourceMeter; we still (re)arm the shared timer so bindings
        // that await external work observe the wall-time timeout.
        if (!CancellationToken.CanBeCanceled)
        {
            var shared = _sharedWallTimeToken ??= new SharedWallTimeTokenSource();
            shared.ArmDeadline(Budget.RemainingWallTime());
            return shared;
        }

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

    private static int CheckedCharLength(int left, int right)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.QuotaExceeded,
                "string byte budget exhausted"));
        }
    }
}

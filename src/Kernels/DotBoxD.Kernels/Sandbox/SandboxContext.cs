using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Sandbox;

public sealed partial class SandboxContext
{
    private DeterministicRandom? _deterministicRandom;
    private BindingReturnCreditTracker? _returnCredits;
    private int _callDepth;

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
        if (!Policy.GrantsCapability(capabilityId, EffectiveGrantClock))
        {
            throw DenyCapability(capabilityId);
        }
    }

    public CapabilityGrant GetCapability(string capabilityId)
    {
        // Single indexed lookup: resolve the grant once and reuse it for the
        // permission decision instead of scanning the grant list twice (once to
        // authorize, once to fetch). The denial audit and error stay identical.
        return Policy.TryGetGrant(capabilityId, EffectiveGrantClock, out var grant)
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

    public void ChargeLogEvent(string message) => Budget.ChargeLogEvent(message);

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

}

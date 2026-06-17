using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime;

using DotBoxD.Kernels;

internal static class CompiledBindingDispatcher
{
    [ThreadStatic] private static ICompiledAwaitPump? _pump;

    internal static IDisposable InstallAwaitPump(ICompiledAwaitPump pump)
    {
        var previous = _pump;
        _pump = pump;
        return new AwaitPumpScope(previous);
    }

    public static SandboxValue CallBinding(SandboxContext context, string id, SandboxValue[] args)
    {
        var descriptor = context.Bindings.GetDescriptor(id);
        var auditCheckpoint = context.AuditCheckpoint();
        using var grantClock = context.BeginBindingGrantClockScope(context.Policy.GrantClock);
        try
        {
            ValidateArguments(descriptor, args);
            context.ChargeBindingCall(descriptor);
            EnsureAsyncGrant(context, descriptor);
        }
        catch (SandboxRuntimeException ex)
        {
            context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, ex.Error.Code);
            throw;
        }

        CancellationTokenSource? timeout = null;
        try
        {
            timeout = context.CreateWallTimeToken();
            using var returnCredits = context.BeginBindingReturnCreditScope(descriptor.ReturnType);
            var value = AwaitBinding(context, descriptor.Invoke(context, args, timeout.Token));
            context.Checkpoint();
            value = context.ChargeBindingReturn(descriptor, value);
            context.EnsureRequiredBindingSuccessAudit(descriptor, auditCheckpoint);
            return value;
        }
        catch (SandboxRuntimeException ex)
        {
            context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, ex.Error.Code);
            throw;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, SandboxErrorCode.Cancelled);
            throw;
        }
        catch (OperationCanceledException) when (timeout?.IsCancellationRequested == true)
        {
            var error = new SandboxError(SandboxErrorCode.Timeout, $"binding '{id}' timed out");
            context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.BindingFailure, $"binding '{id}' failed");
            context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.BindingFailure, $"binding '{id}' failed");
            context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, error.Code);
            throw new SandboxRuntimeException(error);
        }
        finally
        {
            timeout?.Dispose();
        }
    }

    public static SandboxValue CallBinding2(
        SandboxContext context,
        string id,
        SandboxValue arg0,
        SandboxValue arg1)
    {
        var descriptor = context.Bindings.GetDescriptor(id);
        var auditCheckpoint = context.AuditCheckpoint();
        using var grantClock = context.BeginBindingGrantClockScope(context.Policy.GrantClock);
        try
        {
            ValidateArguments(descriptor, arg0, arg1);
            context.ChargeBindingCall(descriptor);
            EnsureAsyncGrant(context, descriptor);
        }
        catch (SandboxRuntimeException ex)
        {
            context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, ex.Error.Code);
            throw;
        }

        CancellationTokenSource? timeout = null;
        try
        {
            timeout = context.CreateWallTimeToken();
            using var returnCredits = context.BeginBindingReturnCreditScope(descriptor.ReturnType);
            var pending = descriptor.Invoke.Target is ITwoArgumentBindingInvoker fastInvoker
                ? fastInvoker.Invoke(context, arg0, arg1, timeout.Token)
                : descriptor.Invoke(context, [arg0, arg1], timeout.Token);
            var value = AwaitBinding(context, pending);
            context.Checkpoint();
            value = context.ChargeBindingReturn(descriptor, value);
            context.EnsureRequiredBindingSuccessAudit(descriptor, auditCheckpoint);
            return value;
        }
        catch (SandboxRuntimeException ex)
        {
            context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, ex.Error.Code);
            throw;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, SandboxErrorCode.Cancelled);
            throw;
        }
        catch (OperationCanceledException) when (timeout?.IsCancellationRequested == true)
        {
            var error = new SandboxError(SandboxErrorCode.Timeout, $"binding '{id}' timed out");
            context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.BindingFailure, $"binding '{id}' failed");
            context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.BindingFailure, $"binding '{id}' failed");
            context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, error.Code);
            throw new SandboxRuntimeException(error);
        }
        finally
        {
            timeout?.Dispose();
        }
    }

    private static void EnsureAsyncGrant(SandboxContext context, BindingDescriptor descriptor)
    {
        if (!descriptor.IsAsync || context.AsyncEnabled)
        {
            return;
        }

        throw new SandboxRuntimeException(new SandboxError(
            SandboxErrorCode.PermissionDenied,
            $"binding '{descriptor.Id}' requires the '{RuntimeCapabilityIds.Async}' capability"));
    }

    private static SandboxValue AwaitBinding(SandboxContext context, ValueTask<SandboxValue> pending)
    {
        // Synchronous bindings (the common case) complete inline, so read the
        // result directly and avoid allocating a Task<SandboxValue> wrapper.
        if (pending.IsCompleted)
        {
            return pending.GetAwaiter().GetResult();
        }

        if (!context.AsyncEnabled)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.BindingFailure,
                "binding returned a pending result; async capability is not granted"));
        }

        var pump = _pump;
        if (pump is null)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.BindingFailure,
                "async pump is not installed"));
        }

        return pump.RunToCompletion(pending);
    }

    private sealed class AwaitPumpScope(ICompiledAwaitPump? previous) : IDisposable
    {
        public void Dispose()
        {
            _pump = previous;
        }
    }

    // Compiled IR was already verified against the binding signature and every
    // argument value reaches this dispatcher having passed full recursive validation
    // at a trust boundary (entrypoint inputs via EntrypointBinder, binding returns via
    // ChargeBindingReturn) and stays typed through every internal constructor. The hot
    // path therefore only needs to confirm the wrapper kind plus declared element
    // metadata via the value's snapshotted Type, not re-walk every nested element on
    // each call. A shallow Type comparison keeps the boundary that distinguishes, e.g.,
    // a scalar from a List<I32> argument while skipping the per-call collection re-walk.
    private static void ValidateArguments(BindingDescriptor descriptor, IReadOnlyList<SandboxValue> args)
    {
        if (args.Count != descriptor.Parameters.Count)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.ValidationError,
                $"binding '{descriptor.Id}' argument count does not match verified plan"));
        }

        for (var i = 0; i < descriptor.Parameters.Count; i++)
        {
            if (!args[i].Type.Equals(descriptor.Parameters[i]))
            {
                throw new SandboxRuntimeException(new SandboxError(
                    SandboxErrorCode.ValidationError,
                    $"binding '{descriptor.Id}' argument type does not match verified plan"));
            }
        }
    }

    private static void ValidateArguments(BindingDescriptor descriptor, SandboxValue arg0, SandboxValue arg1)
    {
        if (descriptor.Parameters.Count != 2)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.ValidationError,
                $"binding '{descriptor.Id}' argument count does not match verified plan"));
        }

        if (!arg0.Type.Equals(descriptor.Parameters[0]) || !arg1.Type.Equals(descriptor.Parameters[1]))
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.ValidationError,
                $"binding '{descriptor.Id}' argument type does not match verified plan"));
        }
    }
}

internal interface ICompiledAwaitPump
{
    SandboxValue RunToCompletion(ValueTask<SandboxValue> pending);
}

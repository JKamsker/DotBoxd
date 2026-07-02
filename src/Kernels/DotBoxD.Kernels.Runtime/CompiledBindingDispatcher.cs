using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime;

using DotBoxD.Kernels;

internal static partial class CompiledBindingDispatcher
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
        var descriptor = context.GetBindingDescriptor(id);
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

    public static SandboxValue CallBinding1(
        SandboxContext context,
        string id,
        SandboxValue arg0)
    {
        var descriptor = context.GetBindingDescriptor(id);
        var auditCheckpoint = context.AuditCheckpoint();
        using var grantClock = context.BeginBindingGrantClockScope(context.Policy.GrantClock);
        try
        {
            ValidateArguments(descriptor, arg0);
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
            var pending = descriptor.Invoke.Target is IOneArgumentBindingInvoker fastInvoker
                ? fastInvoker.Invoke(context, arg0, timeout.Token)
                : descriptor.Invoke(context, [arg0], timeout.Token);
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

    public static SandboxValue CallBinding2(
        SandboxContext context,
        string id,
        SandboxValue arg0,
        SandboxValue arg1)
    {
        var descriptor = context.GetBindingDescriptor(id);
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

}

internal interface ICompiledAwaitPump
{
    SandboxValue RunToCompletion(ValueTask<SandboxValue> pending);
}

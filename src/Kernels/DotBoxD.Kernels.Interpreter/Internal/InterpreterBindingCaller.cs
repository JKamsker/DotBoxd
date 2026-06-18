using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

/// <summary>
/// Invokes a host binding from the interpreter call path, preserving the exact
/// audit-checkpoint, fuel/return charging, wall-time timeout, and failure-audit
/// ordering. Extracted from <see cref="ExpressionEvaluator"/> verbatim so the call
/// dispatcher stays focused while this security-sensitive control flow stays in one
/// cohesive place.
/// </summary>
internal static class InterpreterBindingCaller
{
    /// <summary>
    /// Invokes the host binding identified by <paramref name="descriptor"/>. The
    /// <paramref name="args"/> sequence is caller-owned and may be retained by the host
    /// binding, so it must be a stable, dedicated sequence (never a pooled or reused buffer).
    /// </summary>
    public static async ValueTask<SandboxValue> CallAsync(
        SandboxContext context,
        SandboxExecutionOptions options,
        string moduleHash,
        BindingDescriptor descriptor,
        IReadOnlyList<SandboxValue> args,
        string functionId)
    {
        InterpreterTrace.WriteBindingCall(context, options, moduleHash, functionId, descriptor);
        var auditCheckpoint = context.AuditCheckpoint();
        using var grantClock = context.BeginBindingGrantClockScope(context.Policy.GrantClock);
        try
        {
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
            var pending = descriptor.Invoke(context, args, timeout.Token);
            var value = pending.IsCompleted
                ? pending.GetAwaiter().GetResult()
                : await AwaitPendingAsync(context, pending).ConfigureAwait(false);
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
            var error = new SandboxError(SandboxErrorCode.Timeout, $"binding '{descriptor.Id}' timed out");
            context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.BindingFailure, $"binding '{descriptor.Id}' failed");
            context.EnsureRequiredBindingFailureAudit(descriptor, auditCheckpoint, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.BindingFailure, $"binding '{descriptor.Id}' failed");
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

    private static async ValueTask<SandboxValue> AwaitPendingAsync(
        SandboxContext context,
        ValueTask<SandboxValue> pending)
    {
        if (!context.AsyncEnabled)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.BindingFailure,
                "binding returned a pending result; async capability is not granted"));
        }

        return await pending.ConfigureAwait(false);
    }
}

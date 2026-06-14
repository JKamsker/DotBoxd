namespace DotBoxd.Kernels.Interpreter.Internal;

using DotBoxd.Kernels;

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
    /// Invokes the host binding identified by <paramref name="id"/>. The
    /// <paramref name="args"/> sequence is caller-owned and may be retained by the host
    /// binding, so it must be a stable, dedicated sequence (never a pooled or reused buffer).
    /// </summary>
    public static async ValueTask<SandboxValue> CallAsync(
        SandboxContext context,
        SandboxExecutionOptions options,
        string moduleHash,
        string id,
        IReadOnlyList<SandboxValue> args,
        string functionId)
    {
        var descriptor = context.Bindings.GetDescriptor(id);
        InterpreterTrace.WriteBindingCall(context, options, moduleHash, functionId, descriptor);
        var auditCheckpoint = context.AuditCheckpoint();
        try
        {
            context.ChargeBindingCall(descriptor);
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
            using var returnCredits = context.BeginBindingReturnCreditScope();
            var value = await descriptor.Invoke(context, args, timeout.Token).ConfigureAwait(false);
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
}

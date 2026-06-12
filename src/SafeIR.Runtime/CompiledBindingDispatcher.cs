namespace SafeIR.Runtime;

using SafeIR;

internal static class CompiledBindingDispatcher
{
    public static SandboxValue CallBinding(SandboxContext context, string id, SandboxValue[] args)
    {
        var descriptor = context.Bindings.GetDescriptor(id);
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

        using var timeout = context.CreateWallTimeToken();
        try
        {
            var value = descriptor.Invoke(context, args, timeout.Token).AsTask().GetAwaiter().GetResult();
            context.EnsureRequiredBindingSuccessAudit(descriptor, auditCheckpoint);
            return context.ChargeBindingReturn(descriptor, value);
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
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
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
    }
}

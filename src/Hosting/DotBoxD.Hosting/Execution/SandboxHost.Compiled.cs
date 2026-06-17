using DotBoxD.Hosting.Execution.Compiled;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Execution;

public sealed partial class SandboxHost
{
    private async ValueTask<SandboxExecutionResult> ExecuteCompiledAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        if (!_compiled.IsAvailable || options.EnableDebugTrace)
        {
            var reason = !_compiled.IsAvailable ? CompilerUnavailableError() : DebugTraceFallbackError();
            return options.AllowFallbackToInterpreter
                ? await ExecuteFallbackToInterpreterAsync(
                        plan,
                        entrypoint,
                        input,
                        options,
                        reason,
                        cancellationToken)
                    .ConfigureAwait(false)
                : CompilerUnavailableResult(plan, options, reason);
        }

        var compiled = await TryExecuteCompiledAsync(plan, entrypoint, input, options, cancellationToken)
            .ConfigureAwait(false);
        if (compiled.Result is not null)
        {
            return compiled.Result;
        }

        return await ExecuteFallbackToInterpreterAsync(
                plan,
                entrypoint,
                input,
                options,
                compiled.FallbackReason ?? CompilerUnavailableError(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<SandboxExecutionResult> ExecuteFallbackToInterpreterAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        SandboxError reason,
        CancellationToken cancellationToken)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var fallbackOptions = options with { RunId = runId };
        var result = await ExecuteInterpretedAsync(plan, entrypoint, input, fallbackOptions, cancellationToken)
            .ConfigureAwait(false);
        var audit = new InMemoryAuditSink();
        if (reason.Code == SandboxErrorCode.VerifierFailure)
        {
            audit.Write(VerifierFailureAudit(plan, runId, reason));
        }

        audit.Write(FallbackAudit(plan, runId, reason));
        foreach (var auditEvent in result.AuditEvents)
        {
            audit.Write(auditEvent);
        }

        return result with { AuditEvents = audit.OwnedEventSnapshot() };
    }

    private async ValueTask<CompiledAttempt> TryExecuteCompiledAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var executable = await _compiled.GetAsync(plan, entrypoint, cancellationToken).ConfigureAwait(false);
            var execution = ShouldUseCompiledAsyncWorker(plan, entrypoint)
                ? CompiledExecutionRunner.ExecuteOnWorkerAsync(executable, plan, entrypoint, input, options, cancellationToken)
                : CompiledExecutionRunner.ExecuteAsync(
                    executable,
                    plan,
                    entrypoint,
                    input,
                    options,
                    cancellationToken,
                    ShouldUseCompiledInlineAwaitPump(plan, entrypoint));
            var result = await execution
                .ConfigureAwait(false);
            return new CompiledAttempt(result, null);
        }
        catch (SandboxRuntimeException ex) when (CanFallback(options, ex))
        {
            return new CompiledAttempt(null, ex.Error);
        }
        catch (SandboxRuntimeException ex)
        {
            return new CompiledAttempt(CompiledFailureResult(plan, options, ex.Error), null);
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled");
            return new CompiledAttempt(CompiledFailureResult(plan, options, error), null);
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "compiled execution failed");
            return new CompiledAttempt(CompiledFailureResult(plan, options, error), null);
        }
    }

    private static bool CanFallback(SandboxExecutionOptions options, SandboxRuntimeException ex)
        => options.AllowFallbackToInterpreter &&
           ex.Error.Code is SandboxErrorCode.VerifierFailure or SandboxErrorCode.ValidationError;

    private static SandboxError CompilerUnavailableError()
        => new(SandboxErrorCode.ValidationError, "compiled execution is not available for this run");

    private static SandboxError DebugTraceFallbackError()
        => new(SandboxErrorCode.ValidationError, "compiled execution is disabled while debug tracing is enabled");

    private sealed record CompiledAttempt(SandboxExecutionResult? Result, SandboxError? FallbackReason);
}

namespace DotBoxD.Hosting;

using DotBoxD.Kernels;

internal static class CompiledNoAuditValueRunner
{
    public static PreparedExecutionResult Execute(
        CompiledExecutable executable,
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        IReadOnlySet<string> allowedBindings,
        CancellationToken cancellationToken,
        CompiledNoAuditRunState? reusableState)
    {
        var artifact = executable.Artifact;
        var budget = reusableState?.Budget ?? new ResourceMeter(plan.Budget);
        var context = reusableState?.ContextFor(allowedBindings, cancellationToken) ??
            new SandboxContext(
                SandboxRunId.Suppressed,
                plan.Policy,
                budget,
                plan.Bindings,
                NoopAuditSink.Instance,
                cancellationToken,
                allowedBindings,
                plan.ModuleHash,
                plan.PolicyHash);

        try
        {
            budget.CheckDeadline();
            context.ChargeValue(input);
            var value = artifact.Entrypoint(context, input);
            CompiledExecutionRunner.EnsureReturnType(plan, entrypoint, value);
            return PreparedExecutionResult.FromNoAuditSuccess(
                value,
                ExecutionMode.Compiled,
                artifact.ArtifactHash);
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled");
            return PreparedExecutionResult.FromResult(
                CompiledExecutionRunner.FailureResult(plan, executable, options, budget, error));
        }
        catch (SandboxRuntimeException ex)
        {
            return PreparedExecutionResult.FromResult(
                CompiledExecutionRunner.FailureResult(plan, executable, options, budget, ex.Error));
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "compiled sandbox execution failed");
            return PreparedExecutionResult.FromResult(
                CompiledExecutionRunner.FailureResult(plan, executable, options, budget, error));
        }
    }
}

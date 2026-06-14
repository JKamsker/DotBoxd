namespace DotBoxd.Kernels.Interpreter;

using DotBoxd.Kernels;

public interface ISandboxInterpreter
{
    ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken);
}

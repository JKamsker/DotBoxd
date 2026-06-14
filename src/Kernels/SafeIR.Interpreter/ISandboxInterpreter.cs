namespace SafeIR.Interpreter;

using SafeIR;

public interface ISandboxInterpreter
{
    ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken);
}

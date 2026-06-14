namespace DotBoxD.Hosting;

using DotBoxD.Kernels;

internal readonly record struct PreparedExecutionResult(
    bool Succeeded,
    SandboxValue? Value,
    SandboxError? Error,
    ExecutionMode ActualMode,
    string? ArtifactHash,
    SandboxExecutionResult? FullResult)
{
    public static PreparedExecutionResult FromNoAuditSuccess(
        SandboxValue value,
        ExecutionMode actualMode,
        string? artifactHash)
        => new(
            Succeeded: true,
            value,
            Error: null,
            actualMode,
            artifactHash,
            FullResult: null);

    public static PreparedExecutionResult FromResult(SandboxExecutionResult result)
        => new(
            result.Succeeded,
            result.Value,
            result.Error,
            result.ActualMode,
            result.ArtifactHash,
            result);
}

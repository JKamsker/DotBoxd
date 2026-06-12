using SafeIR.Compiler;
using SafeIR.Compiler.Emitters;
using SafeIR.Verifier;

namespace SafeIR.Tests;

internal sealed class FailingCompiler : ISandboxCompiler
{
    public int Calls { get; private set; }

    public ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken)
    {
        Calls++;
        throw new InvalidOperationException("compiler must not be called");
    }
}

internal sealed class DynamicDelegateCompiler : ISandboxCompiler
{
    public int Calls { get; private set; }
    public bool DelegateExecuted { get; private set; }

    public ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken)
    {
        Calls++;
        return ValueTask.FromResult(CompiledArtifactTestFactory.DynamicMethod(
            plan,
            (_, _) =>
            {
                DelegateExecuted = true;
                return SandboxValue.FromInt32(123);
            },
            "delegate-artifact"));
    }
}

internal sealed class TamperedExecutionDelegateCompiler : ISandboxCompiler
{
    private readonly ReflectionEmitSandboxCompiler _inner = new(new GeneratedAssemblyVerifier());

    public bool DelegateExecuted { get; private set; }

    public async ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken)
    {
        var artifact = await _inner.CompileAsync(plan, options, cancellationToken).ConfigureAwait(false);
        return artifact with
        {
            Entrypoint = (_, _) =>
            {
                DelegateExecuted = true;
                return SandboxValue.FromInt32(999);
            }
        };
    }
}

internal sealed class SandboxFailureCompiler(SandboxErrorCode code) : ISandboxCompiler
{
    public ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken)
        => throw new SandboxRuntimeException(new SandboxError(code, "compiler failed"));
}

internal sealed class UnexpectedFailureCompiler : ISandboxCompiler
{
    public ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException("compiler failed unexpectedly");
}

namespace DotBoxD.Hosting;

using DotBoxD.Kernels;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Compiler.Emitters;

internal sealed class CompiledExecutionProvider(ISandboxCompiler? compiler) : IDisposable
{
    private readonly CompiledArtifactExecutionCache _artifacts = new();
    private readonly CompiledExecutableExecutionCache _executables = new();
    private readonly CompiledExecutableCache _materialized = new();

    public bool IsAvailable => compiler is not null;

    public ValueTask<CompiledExecutable> GetAsync(
        ExecutionPlan plan,
        string entrypoint,
        CancellationToken cancellationToken)
        => compiler is ReflectionEmitSandboxCompiler { UsesPersistentCache: false }
            ? GetCachedReflectionExecutableAsync(plan, entrypoint, cancellationToken)
            : GetCompilerExecutableAsync(plan, entrypoint, cancellationToken);

    public void Dispose() => _materialized.Dispose();

    private async ValueTask<CompiledExecutable> GetCompilerExecutableAsync(
        ExecutionPlan plan,
        string entrypoint,
        CancellationToken cancellationToken)
    {
        var artifact = await compiler!.CompileAsync(plan, new CompileOptions(entrypoint), cancellationToken)
            .ConfigureAwait(false);
        return await _materialized.GetAsync(artifact, plan, entrypoint, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<CompiledExecutable> GetCachedReflectionExecutableAsync(
        ExecutionPlan plan,
        string entrypoint,
        CancellationToken cancellationToken)
    {
        var artifact = await _artifacts.GetAsync(
                plan,
                entrypoint,
                ct => compiler!.CompileAsync(plan, new CompileOptions(entrypoint), ct),
                cancellationToken)
            .ConfigureAwait(false);
        return await _executables.GetAsync(
                plan,
                entrypoint,
                ct => _materialized.GetAsync(artifact, plan, entrypoint, ct),
                cancellationToken)
            .ConfigureAwait(false);
    }
}

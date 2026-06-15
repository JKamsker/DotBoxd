namespace DotBoxD.Hosting;

using DotBoxD.Kernels;

internal static class CompiledEntrypointSupport
{
    public static bool CanCompile(ExecutionPlan plan, string entrypoint)
        => plan.FunctionAnalysis.ContainsKey(entrypoint);
}

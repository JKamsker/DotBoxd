namespace DotBoxd.Hosting;

using DotBoxd.Kernels;
using DotBoxd.Kernels.Compiler;

public interface IExecutionModeSelector
{
    ExecutionModeDecision Choose(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        ModuleHotnessStats hotness,
        CompiledCacheStatus cacheStatus);
}

public sealed record ExecutionModeDecision(ExecutionMode Mode)
{
    public static ExecutionModeDecision Interpreted { get; } = new(ExecutionMode.Interpreted);
    public static ExecutionModeDecision Compiled { get; } = new(ExecutionMode.Compiled);
}

public sealed record ModuleHotnessStats(
    string PlanHash,
    string Entrypoint,
    int RunCount,
    int CompletedRunCount,
    TimeSpan AverageInterpretedDuration,
    long AverageFuelUsed,
    DateTimeOffset? LastRunAt,
    int CompileFailures,
    string? LastCompiledArtifactHash)
{
    public ModuleHotnessStats(int runCount)
        : this(
            string.Empty,
            string.Empty,
            runCount,
            0,
            TimeSpan.Zero,
            0,
            null,
            0,
            null)
    {
    }
}

public sealed class HotnessExecutionModeSelector : IExecutionModeSelector
{
    public ExecutionModeDecision Choose(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        ModuleHotnessStats hotness,
        CompiledCacheStatus cacheStatus)
    {
        var threshold = Math.Max(2, options.AutoCompileThreshold);
        return hotness.RunCount < threshold
            ? ExecutionModeDecision.Interpreted
            : ExecutionModeDecision.Compiled;
    }
}

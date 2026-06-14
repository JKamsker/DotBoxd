namespace SafeIR.Hosting;

using System.Diagnostics;
using SafeIR;

public sealed partial class SandboxHost
{
    private async ValueTask<SandboxExecutionResult> ExecuteAutoAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        if (_compiler is null || options.EnableDebugTrace)
        {
            return await ExecuteInterpretedAsync(plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false);
        }

        var hotness = _autoHotness.BeginAttempt(plan, entrypoint);
        if (hotness.Stats.RunCount == 1)
        {
            return await ExecuteTrackedAutoAsync(
                    hotness,
                    () => ExecuteInterpretedAsync(plan, entrypoint, input, options, cancellationToken))
                .ConfigureAwait(false);
        }

        var decision = _modeSelector.Choose(
            plan,
            options,
            hotness.Stats,
            SafeIR.Compiler.CompiledCacheStatus.None);
        if (decision.Mode == ExecutionMode.Interpreted ||
            decision.Mode == ExecutionMode.Auto ||
            !CanCompileEntrypoint(plan, entrypoint))
        {
            return await ExecuteTrackedAutoAsync(
                    hotness,
                    () => ExecuteInterpretedAsync(plan, entrypoint, input, options, cancellationToken))
                .ConfigureAwait(false);
        }

        if (decision.Mode != ExecutionMode.Compiled)
        {
            return CompleteAutoResult(hotness, InvalidExecutionOptionsResult(
                plan,
                options,
                $"execution mode selector returned unsupported mode '{(int)decision.Mode}'"));
        }

        return await ExecuteTrackedAutoAsync(
                hotness,
                () => ExecuteCompiledAsync(plan, entrypoint, input, options, cancellationToken))
            .ConfigureAwait(false);
    }

    private static async ValueTask<SandboxExecutionResult> ExecuteTrackedAutoAsync(
        AutoHotnessAttempt hotness,
        Func<ValueTask<SandboxExecutionResult>> execute)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await execute().ConfigureAwait(false);
        stopwatch.Stop();
        hotness.Complete(result, stopwatch.Elapsed);
        return result;
    }

    private static SandboxExecutionResult CompleteAutoResult(
        AutoHotnessAttempt hotness,
        SandboxExecutionResult result)
    {
        hotness.Complete(result, TimeSpan.Zero);
        return result;
    }
}

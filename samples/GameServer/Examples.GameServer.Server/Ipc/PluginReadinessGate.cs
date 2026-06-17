using System.Globalization;

namespace DotBoxD.Kernels.Game.Server.Ipc;

internal enum PluginReadinessWaitResult
{
    Completed,
    PluginExited,
    TimedOut
}

internal static class PluginReadinessGate
{
    public const string TimeoutMillisecondsEnvVar = "DOTBOXD_GAME_PLUGIN_READINESS_TIMEOUT_MS";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public static TimeSpan ReadTimeout()
    {
        var configured = Environment.GetEnvironmentVariable(TimeoutMillisecondsEnvVar);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DefaultTimeout;
        }

        return int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds) &&
               milliseconds > 0
            ? TimeSpan.FromMilliseconds(milliseconds)
            : DefaultTimeout;
    }

    public static async Task<PluginReadinessWaitResult> WaitAsync(
        Task readiness,
        Task pluginExit,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(pluginExit);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be positive.");
        }

        if (readiness.IsCompleted)
        {
            await readiness.ConfigureAwait(false);
            return PluginReadinessWaitResult.Completed;
        }

        if (pluginExit.IsCompleted)
        {
            await pluginExit.ConfigureAwait(false);
            return PluginReadinessWaitResult.PluginExited;
        }

        using var timeoutCancellation = new CancellationTokenSource();
        var timeoutTask = Task.Delay(timeout, timeoutCancellation.Token);
        var completed = await Task.WhenAny(readiness, pluginExit, timeoutTask).ConfigureAwait(false);
        if (completed != timeoutTask)
        {
            timeoutCancellation.Cancel();
        }

        if (completed == readiness)
        {
            await readiness.ConfigureAwait(false);
            return PluginReadinessWaitResult.Completed;
        }

        if (completed == pluginExit)
        {
            await pluginExit.ConfigureAwait(false);
            return PluginReadinessWaitResult.PluginExited;
        }

        return PluginReadinessWaitResult.TimedOut;
    }
}

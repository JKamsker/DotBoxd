using SafeIR.Plugins;

namespace SafeIR.Tests;

internal sealed class BlockingPluginMessageSink : IPluginMessageSink
{
    public TaskCompletionSource SendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseSend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask SendAsync(string targetId, string message, CancellationToken cancellationToken = default)
    {
        SendStarted.TrySetResult();
        await ReleaseSend.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal static class PluginAddendumTestPolicies
{
    public static PluginServer CreateServer(
        IPluginMessageSink? messages = null,
        ExecutionMode executionMode = ExecutionMode.Auto)
        => PluginServer.Create(messages, defaultPolicy: LongWall(), executionMode: executionMode);

    public static SandboxPolicy LongWall()
        => SandboxPolicyBuilder.Create().GrantLogging().GrantGameMessageWrite()
            .WithFuel(100_000).WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10)).Build();
}

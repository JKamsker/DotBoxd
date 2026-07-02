using System.Reflection;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerSurpriseRegressionTests
{
    [Fact]
    public async Task Generated_plugin_server_concurrent_StartAsync_does_not_start_twice()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(BaseServerSource(extraPluginTypes: """

                public static class StartProbe
                {
                    public static async Task<int> CountStartsBeforeFirstConnectionCompletesAsync()
                    {
                        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        var release = new TaskCompletionSource<DotBoxD.Services.Peer.RpcPeerSession>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        var starts = 0;
                        var server = new RemotePluginServer((_, _) =>
                        {
                            Interlocked.Increment(ref starts);
                            entered.TrySetResult();
                            return new ValueTask<DotBoxD.Services.Peer.RpcPeerSession>(release.Task);
                        });

                        var first = server.StartAsync().AsTask();
                        await entered.Task.WaitAsync(System.TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        var second = server.StartAsync().AsTask();
                        await Task.Delay(100).ConfigureAwait(false);
                        var observed = Volatile.Read(ref starts);
                        release.SetException(new System.OperationCanceledException("stop"));
                        await ObserveFaultAsync(first).ConfigureAwait(false);
                        await ObserveFaultAsync(second).ConfigureAwait(false);
                        return observed;
                    }

                    private static async Task ObserveFaultAsync(Task task)
                    {
                        try
                        {
                            await task.ConfigureAwait(false);
                        }
                        catch (System.OperationCanceledException)
                        {
                        }
                    }
                }
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        var assembly = Emit(outputCompilation);
        var probe = assembly.GetType("Regression.Plugin.StartProbe", throwOnError: true)!;
        var method = probe.GetMethod(
            "CountStartsBeforeFirstConnectionCompletesAsync",
            BindingFlags.Public | BindingFlags.Static)!;

        var observed = await Assert.IsAssignableFrom<Task<int>>(method.Invoke(null, null));

        Assert.Equal(1, observed);
    }
}

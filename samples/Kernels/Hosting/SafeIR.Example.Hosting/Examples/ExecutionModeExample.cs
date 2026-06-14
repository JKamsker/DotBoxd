namespace SafeIR.Example.Hosting;

using SafeIR;
using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.Plugins;

internal static class ExecutionModeExample
{
    public static async Task RunAsync()
    {
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled, ExecutionMode.Auto }) {
            var messages = new InMemoryPluginMessageSink();
            var server = PluginServer.Create(
                messages,
                defaultPolicy: PluginExamplePolicies.MessageWrite(),
                executionMode: mode);
            server.RegisterEventAdapter(DamageEventAdapter.Instance);
            var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
            server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
            await server.Hooks.PublishAsync(new DamageEvent("fire", 120, $"player-{mode}"));
            Console.WriteLine(
                $"execution mode {mode}: messages={messages.Messages.Count}, requested={kernel.LastExecution?.RequestedMode}");
        }
    }
}

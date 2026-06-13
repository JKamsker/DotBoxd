namespace SafeIR.AddendumExamples.Examples;

using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.Plugins;

internal static class KernelClassExample
{
    public static async Task RunAsync()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(messages, defaultPolicy: PluginExamplePolicies.MessageWrite());
        server.RegisterEventAdapter(DamageEventAdapter.Instance);

        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");
        kernel.Value.MinDamage = 250;
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-2"));

        await kernel.ModifyAsync(state => {
            state.DamageType = "ice";
            state.MinDamage = 250;
        });
        await server.Hooks.PublishAsync(new DamageEvent("ice", 300, "player-3"));

        Console.WriteLine($"kernel class: minDamage={kernel.Value.MinDamage}, messages={messages.Messages.Count}");
    }
}

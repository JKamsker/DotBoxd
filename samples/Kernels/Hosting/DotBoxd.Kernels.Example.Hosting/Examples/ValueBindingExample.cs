namespace DotBoxd.Kernels.Example.Hosting;

using DotBoxd.Kernels.PluginIpc.Server.Abstractions;
using DotBoxd.Plugins;

internal static class ValueBindingExample
{
    public static async Task RunAsync()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(messages);
        server.RegisterEventAdapter(DamageEventAdapter.Instance);
        var damageType = server.BindValue("damageType", "fire");
        var minDamage = server.BindValue("minDamage", 100);

        server.Hooks.On<DamageEvent>()
            .Where((e, _) => e.DamageType == damageType.Value)
            .Where((e, _) => e.Amount >= minDamage.Value)
            .InvokeHostHandler((e, ctx) => HandleDamage(e, ctx, "value binding matched"));

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));
        damageType.Value = "ice";
        minDamage.Value = 250;
        await server.Hooks.PublishAsync(new DamageEvent("fire", 300, "player-2"));
        await server.Hooks.PublishAsync(new DamageEvent("ice", 300, "player-3"));

        Console.WriteLine($"value bindings: messages={messages.Messages.Count}");
    }

    private static void HandleDamage(DamageEvent e, HookContext ctx, string message)
        => ctx.Messages.Send(e.TargetId, message);
}

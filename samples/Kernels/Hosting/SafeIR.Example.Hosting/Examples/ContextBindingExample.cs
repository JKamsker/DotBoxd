namespace SafeIR.Example.Hosting;

using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.Plugins;

internal static class ContextBindingExample
{
    public static async Task RunAsync()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(messages);
        server.RegisterEventAdapter(DamageEventAdapter.Instance);
        var settings = server.BindContext<DamageSettings>(
            "damage",
            value =>
            {
                value.Enabled = true;
                value.DamageType = "fire";
                value.MinDamage = 100;
            });

        server.Hooks.On<DamageEvent>()
            .Where((_, _) => settings.Value.Enabled)
            .Where((e, _) => e.DamageType == settings.Value.DamageType)
            .Where((e, _) => e.Amount >= settings.Value.MinDamage)
            .InvokeHostHandler((e, ctx) => ctx.Messages.Send(e.TargetId, "context binding matched"));

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));
        settings.Value.DamageType = "ice";
        settings.Value.MinDamage = 250;
        await server.Hooks.PublishAsync(new DamageEvent("fire", 300, "player-2"));
        await server.Hooks.PublishAsync(new DamageEvent("ice", 300, "player-3"));

        Console.WriteLine($"context binding: damageType={settings.Value.DamageType}, messages={messages.Messages.Count}");
    }
}

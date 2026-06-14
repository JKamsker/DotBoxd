namespace SafeIR.Example.PluginAuthoring;

using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.Plugins;

internal static class JsonUploadExample
{
    public static async Task RunAsync()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(messages, defaultPolicy: PluginExamplePolicies.MessageWrite());
        server.RegisterEventAdapter(DamageEventAdapter.Instance);

        var uploadJson = PluginPackageJsonSerializer.Export(FireDamagePluginPackage.Create());
        await server.InstallJsonAsync(uploadJson);
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-json"));

        Console.WriteLine($"json upload: messages={messages.Messages.Count}");
    }
}

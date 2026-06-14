namespace DotBoxd.Kernels.Example.PluginAuthoring;

using DotBoxd.Kernels.PluginIpc.Server.Abstractions;
using DotBoxd.Plugins;

internal static class DesignGuidanceExample
{
    public static async Task RunAsync()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(messages, defaultPolicy: PluginExamplePolicies.MessageWrite());
        server.RegisterEventAdapter(MyEventAdapter.Instance);

        await server.InstallAsync(MyPluginPackage.Create());
        server.Hooks.On<MyEvent>().UseKernel<MyKernel>();
        await server.Hooks.PublishAsync(new MyEvent(150, "player-1"));

        var kernel = server.Kernels.Get<MyKernel>("threshold-guidance");
        kernel.Value.Threshold = 200;
        await server.Hooks.PublishAsync(new MyEvent(150, "player-2"));

        Console.WriteLine($"design guidance: threshold={kernel.Value.Threshold}, messages={messages.Messages.Count}");
    }
}

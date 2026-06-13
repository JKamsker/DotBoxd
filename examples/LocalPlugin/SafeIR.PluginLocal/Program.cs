using SafeIR;
using SafeIR.PluginLocal;
using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.Plugins;

var messages = new InMemoryPluginMessageSink();
var server = PluginServer.Create(messages, defaultPolicy: PluginPolicy());
server.RegisterEventAdapter(DamageEventAdapter.Instance);

var serverGate = server.BindValue("serverGateMinDamage", 0);
var groupedSettings = server.BindContext<IFireDamageSettings>(
    "operatorDefaults",
    settings => {
        settings.DamageType = "fire";
        settings.MinDamage = 100;
    });

await server.InstallAsync(FireDamagePluginPackage.Create());
server.Hooks.On<DamageEvent>()
    .Where((e, _) => e.Amount >= serverGate.Value)
    .UseKernel<FireDamageKernel>();

await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));
var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");
await kernel.ModifyAsync(state => {
    state.MinDamage = 250;
    state.DamageType = "ice";
});
await server.Hooks.PublishAsync(new DamageEvent("ice", 300, "player-2"));

Console.WriteLine("Live context defaults:");
Console.WriteLine($"  {groupedSettings.Value.DamageType} >= {groupedSettings.Value.MinDamage}");
Console.WriteLine("Messages:");
foreach (var message in messages.Messages) {
    Console.WriteLine($"  {message.TargetId}: {message.Message}");
}

static SandboxPolicy PluginPolicy()
    => SandboxPolicyBuilder.Create()
        .GrantLogging()
        .GrantHostMessageWrite()
        .WithFuel(100_000)
        .WithMaxHostCalls(1_000)
        .Build();

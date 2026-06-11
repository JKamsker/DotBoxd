using SafeIR.PluginIpc.Shared;
using SafeIR.Transport.Ipc;
using ShaRPC.Generated;

var pipeName = args.Length > 0 ? args[0] : "safe-ir-plugin-ipc";
await using var connection = await SafeIrShaRpcMessagePackIpc.ConnectNamedPipeAsync(pipeName);
var service = connection.Peer.GetPluginControlService();
Console.WriteLine("Initial settings:");
foreach (var setting in await service.GetSettingsAsync()) {
    Console.WriteLine($"  {setting.Name} = {setting.Value}");
}

await PrintMessagesAsync("fire 120", new DamageEventRequest {
    DamageType = "fire",
    Amount = 120,
    TargetId = "player-1"
});

await service.ModifySettingsAsync(
    [
        new LiveSettingUpdate { Name = "MinDamage", Value = "250" },
        new LiveSettingUpdate { Name = "DamageType", Value = "ice" }
    ],
    atomic: true);
await PrintMessagesAsync("fire 120 after batch update", new DamageEventRequest {
    DamageType = "fire",
    Amount = 120,
    TargetId = "player-1"
});

await PrintMessagesAsync("ice 300 after batch update", new DamageEventRequest {
    DamageType = "ice",
    Amount = 300,
    TargetId = "player-2"
});

async Task PrintMessagesAsync(string label, DamageEventRequest request)
{
    var messages = await service.PublishDamageAsync(request);
    Console.WriteLine(label + ": " + (messages.Count == 0 ? "<no messages>" : string.Join(", ", messages)));
}

using SafeIR.PluginIpc.Shared;
using SafeIR.Transport.Ipc;

var pipeName = args.Length > 0 ? args[0] : "safe-ir-plugin-ipc";
await using var connection = await SafeIrShaRpcMessagePackIpc.ConnectNamedPipeAsync(pipeName);
var service = connection.Get<IPluginControlService>();
Console.WriteLine("Initial settings:");
foreach (var setting in await service.GetSettingsAsync()) {
    Console.WriteLine($"  {setting.Name} = {setting.Value}");
}

await PrintMessagesAsync("fire 120", new DamageEventRequest("fire", 120, "player-1"));

await service.ModifySettingsAsync(
    [
        new LiveSettingUpdate("MinDamage", "250"),
        new LiveSettingUpdate("DamageType", "ice")
    ],
    atomic: true);
await PrintMessagesAsync("fire 120 after batch update", new DamageEventRequest("fire", 120, "player-1"));

await PrintMessagesAsync("ice 300 after batch update", new DamageEventRequest("ice", 300, "player-2"));

async Task PrintMessagesAsync(string label, DamageEventRequest request)
{
    var messages = await service.PublishDamageAsync(request);
    Console.WriteLine(label + ": " + (messages.Length == 0 ? "<no messages>" : string.Join(", ", messages)));
}

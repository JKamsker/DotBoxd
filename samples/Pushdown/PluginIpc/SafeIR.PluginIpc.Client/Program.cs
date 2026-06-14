using SafeIR.PluginIpc.Shared;
using SafeIR.Transport.Ipc;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: SafeIR.PluginIpc.Client <named-pipe-name>");
    return;
}

var pipeName = args[0];
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

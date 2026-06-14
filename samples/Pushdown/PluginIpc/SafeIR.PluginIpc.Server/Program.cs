using SafeIR.PluginIpc.Server;
using SafeIR.Transport.Ipc;
using ShaRPC.Generated;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: SafeIR.PluginIpc.Server <named-pipe-name>");
    return;
}

var pipeName = args[0];
var service = await PluginControlService.CreateAsync();

await using var host = SafeIrShaRpcMessagePackIpc.ListenNamedPipe(
    pipeName,
    peer => peer.ProvidePluginControlService(service));

await host.StartAsync();
Console.WriteLine($"SafeIR plugin IPC server listening on pipe '{pipeName}'.");
Console.WriteLine("Press Ctrl+C to stop.");

var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, e) => {
    e.Cancel = true;
    stopped.TrySetResult();
};

await stopped.Task;
await host.StopAsync();

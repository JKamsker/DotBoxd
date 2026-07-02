namespace DotBoxD.Kernels.Game.Client;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!TryParse(args, out var host, out var port, out var pluginsRoot))
        {
            await Console.Error.WriteLineAsync(
                "Usage: Examples.GameServer.Client --connect <host> <port> --plugins <plugins-root>");
            return 1;
        }

        await using var runtime = await GameClientRuntime.ConnectAsync(host, port, pluginsRoot)
            .ConfigureAwait(false);
        await runtime.InstallClientBundlesAsync().ConfigureAwait(false);
        await runtime.PrintSnapshotAsync().ConfigureAwait(false);

        await Task.Delay(800).ConfigureAwait(false);
        await runtime.ClaimAsync("monster-1").ConfigureAwait(false);
        await runtime.ClaimAsync("monster-1").ConfigureAwait(false);
        await runtime.CallUnknownOperationAsync().ConfigureAwait(false);

        await Task.Delay(1_500).ConfigureAwait(false);
        await runtime.ClaimAsync("monster-2").ConfigureAwait(false);
        await runtime.ClaimAsync("monster-3").ConfigureAwait(false);
        await runtime.ClaimAsync("monster-4").ConfigureAwait(false);

        await runtime.HoldUntilShutdownAsync().ConfigureAwait(false);
        return 0;
    }

    private static bool TryParse(
        string[] args,
        out string host,
        out int port,
        out string pluginsRoot)
    {
        host = "127.0.0.1";
        port = 0;
        pluginsRoot = string.Empty;
        if (args.Length != 5 ||
            !string.Equals(args[0], "--connect", StringComparison.Ordinal) ||
            !int.TryParse(args[2], out port) ||
            !string.Equals(args[3], "--plugins", StringComparison.Ordinal))
        {
            return false;
        }

        host = args[1];
        pluginsRoot = Path.GetFullPath(args[4]);
        return port > 0 && Directory.Exists(pluginsRoot);
    }
}

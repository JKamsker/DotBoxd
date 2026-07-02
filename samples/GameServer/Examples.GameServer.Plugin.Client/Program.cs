namespace DotBoxD.Kernels.Game.Plugin.Client;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[0], "--export", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Usage: Examples.GameServer.Plugin.Client --export <plugins-root>");
            return 1;
        }

        PackageExport.ExportAll(args[1]);
        Console.WriteLine($"[plugin-export] client bundles exported to '{Path.GetFullPath(args[1])}'.");
        return 0;
    }
}

namespace DotBoxD.Kernels.Game.Plugin.Authoring;

/// <summary>
/// Sample host helper for the common single-pipe plugin entrypoint. Custom hosts can skip it and build the
/// generated server directly.
/// </summary>
public static class GamePluginServerHost
{
    public static string PipeNameFromArgs(string[] args)
        => args.Length >= 1 ? args[0] : throw new ArgumentException("Usage: <plugin> <named-pipe-name>");
}

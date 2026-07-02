namespace DotBoxD.Kernels.Game.Client.Rendering;

internal sealed class HudMessageSink : IPluginMessageSink
{
    private readonly ConsoleHudRenderer _renderer;
    private readonly string[] _skullFrames;

    public HudMessageSink(ConsoleHudRenderer renderer, string pluginsRoot, string skullBundleId = "bounty-hunter")
    {
        _renderer = renderer;
        _skullFrames = LoadSkullFrames(pluginsRoot, skullBundleId);
    }

    public void Send(string targetId, string message) => Render(targetId, message);

    public ValueTask SendAsync(string targetId, string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Render(targetId, message);
        return ValueTask.CompletedTask;
    }

    private void Render(string targetId, string message)
    {
        if (message.StartsWith("fx:skull:", StringComparison.Ordinal))
        {
            foreach (var frame in _skullFrames)
            {
                _renderer.Write(targetId, frame);
            }

            return;
        }

        _renderer.Write(targetId, message);
    }

    private static string[] LoadSkullFrames(string pluginsRoot, string bundleId)
    {
        var path = Path.Combine(pluginsRoot, bundleId, "client", "assets", "skull.anim.txt");
        return File.Exists(path) ? File.ReadAllLines(path) : ["skull"];
    }
}

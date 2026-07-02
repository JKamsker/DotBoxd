namespace DotBoxD.Kernels.Game.Client.Rendering;

internal sealed class HudMessageSink : IPluginMessageSink
{
    private readonly ConsoleHudRenderer _renderer;
    private readonly string _pluginsRoot;

    public HudMessageSink(ConsoleHudRenderer renderer, string pluginsRoot)
    {
        _renderer = renderer;
        _pluginsRoot = pluginsRoot;
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
            foreach (var frame in SkullFrames())
            {
                _renderer.Write(targetId, frame);
            }

            return;
        }

        _renderer.Write(targetId, message);
    }

    private string[] SkullFrames()
    {
        var path = Path.Combine(_pluginsRoot, "bounty-hunter", "client", "assets", "skull.anim.txt");
        return File.Exists(path) ? File.ReadAllLines(path) : ["skull"];
    }
}

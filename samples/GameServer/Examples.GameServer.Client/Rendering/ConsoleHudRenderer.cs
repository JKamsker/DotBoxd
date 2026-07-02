namespace DotBoxD.Kernels.Game.Client.Rendering;

internal sealed class ConsoleHudRenderer
{
    private readonly object _gate = new();
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_gate)
            {
                return _lines.ToArray();
            }
        }
    }

    public void Write(string channel, string text)
    {
        var line = $"[client:{channel}] {text}";
        lock (_gate)
        {
            _lines.Add(line);
        }

        Console.WriteLine(line);
    }
}

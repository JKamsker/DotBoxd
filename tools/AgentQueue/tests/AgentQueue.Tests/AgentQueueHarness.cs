using AgentQueue.Core;

namespace AgentQueue.Tests;

internal sealed class AgentQueueHarness : IDisposable
{
    private readonly Queue<string> outputs = new();

    public AgentQueueHarness()
    {
        Root = Path.Combine(Path.GetTempPath(), "agentq-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.Combine(Root, ".git"));
    }

    public string Root { get; }

    public string LastOutput { get; private set; } = string.Empty;

    public string LastError { get; private set; } = string.Empty;

    public int Run(params string[] args)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        AgentQueueApp app = new(output, error, new TestClock());
        int exitCode = app.Run(args, Root);
        LastOutput = output.ToString();
        LastError = error.ToString();
        outputs.Enqueue(LastOutput);
        return exitCode;
    }

    public string WriteBody(string content)
    {
        string path = Path.Combine(Root, "body.md");
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class TestClock : ISystemClock
    {
        public DateTimeOffset UtcNow => new(2026, 6, 12, 10, 0, 0, TimeSpan.Zero);
    }
}

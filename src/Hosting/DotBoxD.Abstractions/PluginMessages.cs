namespace DotBoxD.Abstractions;

public sealed record PluginMessage(string TargetId, string Message);

public interface IPluginMessageSink
{
    void Send(string targetId, string message)
        => SendAsync(targetId, message).AsTask().GetAwaiter().GetResult();

    ValueTask SendAsync(string targetId, string message, CancellationToken cancellationToken = default);
}

public sealed class InMemoryPluginMessageSink : IPluginMessageSink
{
    private const int DefaultMaxMessages = 4096;

    private readonly object _gate = new();
    private readonly List<PluginMessage> _messages = [];
    private readonly int _maxMessages;

    public InMemoryPluginMessageSink()
        : this(DefaultMaxMessages)
    {
    }

    public InMemoryPluginMessageSink(int maxMessages)
    {
        if (maxMessages <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMessages), "Message sink capacity must be positive.");
        }

        _maxMessages = maxMessages;
    }

    public IReadOnlyList<PluginMessage> Messages
    {
        get
        {
            lock (_gate)
            {
                return _messages.ToArray();
            }
        }
    }

    public void Send(string targetId, string message)
        => AddMessage(targetId, message);

    public ValueTask SendAsync(string targetId, string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AddMessage(targetId, message);
        return ValueTask.CompletedTask;
    }

    private void AddMessage(string targetId, string message)
    {
        lock (_gate)
        {
            if (_messages.Count >= _maxMessages)
            {
                throw new InvalidOperationException("The plugin message sink has reached its configured capacity.");
            }

            _messages.Add(new PluginMessage(targetId, message));
        }
    }
}

namespace SafeIR.Server.Abstractions;

using SafeIR;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PluginAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class LiveSettingAttribute : Attribute;

public interface IEventKernel<TEvent>
{
    bool ShouldHandle(TEvent e, HookContext context);

    void Handle(TEvent e, HookContext context);
}

public interface IPluginEventAdapter<in TEvent>
{
    string EventName { get; }
    IReadOnlyList<Parameter> Parameters { get; }
    IReadOnlyList<SandboxValue> ToSandboxValues(TEvent e);
}

public sealed record PluginMessage(string TargetId, string Message);

public interface IPluginMessageSink
{
    void Send(string targetId, string message)
        => SendAsync(targetId, message).AsTask().GetAwaiter().GetResult();

    ValueTask SendAsync(string targetId, string message, CancellationToken cancellationToken = default);
}

public sealed class InMemoryPluginMessageSink : IPluginMessageSink
{
    private readonly List<PluginMessage> _messages = [];
    private readonly IReadOnlyList<PluginMessage> _readOnlyMessages;

    public InMemoryPluginMessageSink()
        => _readOnlyMessages = _messages.AsReadOnly();

    public IReadOnlyList<PluginMessage> Messages => _readOnlyMessages;

    public void Send(string targetId, string message)
        => _messages.Add(new PluginMessage(targetId, message));

    public ValueTask SendAsync(string targetId, string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _messages.Add(new PluginMessage(targetId, message));
        return ValueTask.CompletedTask;
    }
}

public sealed class HookContext
{
    public HookContext(IPluginMessageSink messages, CancellationToken cancellationToken)
    {
        Messages = messages;
        CancellationToken = cancellationToken;
    }

    public IPluginMessageSink Messages { get; }
    public CancellationToken CancellationToken { get; }
}

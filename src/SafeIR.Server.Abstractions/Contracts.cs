namespace SafeIR.Server.Abstractions;

using SafeIR;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PluginAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class LiveSettingAttribute : Attribute;

/// <summary>
/// Marks a host-service method as a sandbox binding the SafeIR generator may call from verified kernel
/// IR. A kernel reaches the service through <see cref="HookContext.Host{THost}"/> (e.g.
/// <c>ctx.Host&lt;IGameWorldAccess&gt;().GetHealth(id)</c>); the generator lowers that call to a
/// <c>CallExpression(<paramref name="bindingId"/>, …)</c>, records <paramref name="capability"/> in the
/// manifest's required capabilities, and adds <paramref name="effects"/> to the manifest's effects. The
/// host registers a matching binding (same id, capability, and effects) so install-time policy and
/// effect validation gate the call.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HostBindingAttribute(string bindingId, string capability, SandboxEffect effects) : Attribute
{
    /// <summary>The sandbox binding id the call lowers to (e.g. <c>host.world.getHealth</c>).</summary>
    public string BindingId { get; } = bindingId;

    /// <summary>The capability the call requires (e.g. <c>game.world.monster.read.health</c>).</summary>
    public string Capability { get; } = capability;

    /// <summary>
    /// The sandbox effects the binding declares — must equal the registered binding's effects so the
    /// manifest's effects match the verified entrypoint effects (a read is
    /// <c>SandboxEffect.Cpu | SandboxEffect.HostStateRead</c>).
    /// </summary>
    public SandboxEffect Effects { get; } = effects;
}

/// <summary>
/// Gates an event property behind a capability. Reading the property from kernel IR contributes
/// <paramref name="id"/> to the manifest's required capabilities, so a kernel that touches the property
/// only installs under a policy granting it. Unannotated properties stay ungated (default-allow).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CapabilityAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}

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

    /// <summary>
    /// Identifies a host service the kernel calls into. In verified kernel IR the call is replaced by a
    /// sandbox binding (see <see cref="HostBindingAttribute"/>), so this marker is never invoked
    /// directly; calling it at runtime throws.
    /// </summary>
    public THost Host<THost>()
        where THost : class
        => throw new NotSupportedException(
            $"Host service '{typeof(THost)}' is reached through a SafeIR sandbox binding; " +
            "ctx.Host<T>() is a lowering marker and is not callable directly.");
}

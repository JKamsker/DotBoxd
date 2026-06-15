namespace DotBoxD.Abstractions;

using DotBoxD.Kernels;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PluginAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class LiveSettingAttribute : Attribute;

/// <summary>
/// Marks a host-service method as a sandbox binding the DotBoxD.Kernels generator may call from verified kernel
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

/// <summary>
/// Marks a reusable helper method whose body the DotBoxD.Kernels generator <b>inlines</b> into the kernel/hook IR
/// at every call site, so plugin authors can factor shared gate/handler logic out of a
/// <c>Where</c>/<c>Select</c>/<c>InvokeKernel</c> lambda (or a kernel-class <c>ShouldHandle</c>/<c>Handle</c>)
/// without leaving the sandbox. For example:
/// <code>
/// server.Hooks.On&lt;MonsterAggroEvent&gt;()
///     .Where((e, ctx) => IsBullying(e.MonsterLevel, e.PlayerLevel))
///     .InvokeKernel((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
///
/// [KernelMethod]
/// public static bool IsBullying(int monsterLevel, int playerLevel) =&gt; monsterLevel - playerLevel &gt;= 3;
/// </code>
/// The call lowers exactly as if the body were written inline: each parameter is replaced by its
/// already-lowered argument IR, and any <c>[HostBinding]</c> calls or <c>[Capability]</c>-gated reads
/// inside the body contribute their capabilities to the calling kernel's manifest.
/// <para>
/// Constraints (verified at generation time; a violation fails the chain/kernel safely rather than
/// miscompiling): the method must be <c>static</c>, have an expression body or a single
/// <c>return</c> statement, and use only the supported scalar types (<c>bool</c>, <c>int</c>,
/// <c>long</c>, <c>double</c>, <c>string</c>) for its parameters and return. Recursion is not allowed.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class KernelMethodAttribute : Attribute;

/// <summary>
/// Marks a class as a <b>kernel RPC service</b>: a batch operation the plugin ships as verified IR and
/// the server runs request/response in a single roundtrip, so a loop over many entities executes
/// server-side (calling the host's existing bindings) instead of one network call per entity. The
/// generator lowers the class's single public batch method — its body may use locals, a <c>foreach</c>
/// over a list parameter, host bindings via <c>ctx.Host&lt;T&gt;()</c>, and may build and return complex
/// objects (records/DTOs) and lists of them. For example:
/// <code>
/// [KernelRpcService("monster-killer")]
/// public sealed partial class MonsterKillerKernel
/// {
///     public List&lt;KillResult&gt; KillMonsters(List&lt;int&gt; monsterIds, HookContext ctx)
///     {
///         var results = new List&lt;KillResult&gt;();
///         foreach (var id in monsterIds)
///             results.Add(new KillResult(id, ctx.Host&lt;IGameWorld&gt;().Kill(id)));
///         return results;
///     }
/// }
/// public readonly record struct KillResult(int MonsterId, bool Success);
/// </code>
/// The trailing <see cref="HookContext"/> parameter is the lowering marker for host bindings (as in a
/// kernel's <c>Handle</c>) and is not part of the wire signature. Parameters, return type, and DTO
/// fields must use the supported scalar types, lists, or nested DTOs.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class KernelRpcServiceAttribute(string id) : Attribute
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

/// <summary>
/// Optional low-allocation event adapter path. Implement this when event values can be written
/// directly into the runtime input buffer; <see cref="EventValueCount"/> must match
/// <see cref="IPluginEventAdapter{TEvent}.Parameters"/>.
/// </summary>
public interface IPluginEventValueWriter<in TEvent> : IPluginEventAdapter<TEvent>
{
    int EventValueCount { get; }
    SandboxValue ToSandboxValue(TEvent e, int index);
    void CopySandboxValues(TEvent e, SandboxValue[] destination, int destinationIndex);
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
            $"Host service '{typeof(THost)}' is reached through a DotBoxD.Kernels sandbox binding; " +
            "ctx.Host<T>() is a lowering marker and is not callable directly.");
}

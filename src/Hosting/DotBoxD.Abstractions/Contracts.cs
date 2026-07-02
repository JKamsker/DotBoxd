using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Abstractions;

using DotBoxD.Kernels;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PluginAttribute : Attribute
{
    public PluginAttribute(string id) => Id = id;

    public string Id { get; }
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EventKernelAttribute(string? id = null) : Attribute
{
    public string? Id { get; } = id;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class LiveSettingAttribute : Attribute;

/// <summary>
/// Marks a host-service method as a sandbox binding the DotBoxD.Kernels generator may call from verified kernel
/// IR. A kernel reaches the service through <see cref="HookContext.Host{THost}"/> or through a
/// constructor-injected service field (e.g. <c>_world.GetHealth(id)</c>); the generator lowers that call to a
/// <c>CallExpression(<paramref name="bindingId"/>, …)</c>, records <paramref name="capability"/> in the
/// manifest's required capabilities, and adds <paramref name="effects"/> to the manifest's effects. The
/// host registers a matching binding (same id, capability, and effects) so install-time policy and
/// effect validation gate the call. Set <see cref="IsAsync"/> when that registered binding declares
/// asynchronous host work.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
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

    /// <summary>
    /// True when the registered sandbox binding uses asynchronous host work even if its declared
    /// effects are otherwise ordinary read/write effects.
    /// </summary>
    public bool IsAsync { get; set; }
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
/// <c>Where</c>/<c>Select</c>/<c>Run</c> lambda (or a kernel-class <c>ShouldHandle</c>/<c>Handle</c>)
/// without leaving the sandbox. The helper can be a static method, or an instance method called on the generated
/// server-context parameter. For example:
/// <code>
/// server.Hooks.On&lt;MonsterAggroEvent&gt;()
///     .Where((e, ctx) => IsBullying(e.MonsterLevel, e.PlayerLevel))
///     .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
///
/// [KernelMethod]
/// public static bool IsBullying(int monsterLevel, int playerLevel) =&gt; monsterLevel - playerLevel &gt;= 3;
/// </code>
/// The call lowers exactly as if the body were written inline: each parameter is replaced by its
/// already-lowered argument IR, and any <c>[HostBinding]</c> calls or <c>[Capability]</c>-gated reads
/// inside the body contribute their capabilities to the calling kernel's manifest.
/// <para>
/// Static helpers may be called through ordinary static-call syntax or extension-method syntax, and
/// named arguments plus supported optional parameter defaults are bound before lowering.
/// </para>
/// <para>
/// Constraints (verified at generation time; a violation fails the chain/kernel safely rather than
/// miscompiling): the method must be static or called on the server-context parameter, have an expression body
/// or a single <c>return</c> statement, and use types the kernel marshaller can represent in the current
/// lowering surface (scalars, supported nullable scalars, enums, GUIDs, records/DTOs, lists, and maps where
/// that surface supports them). Recursion, generic helpers, <c>ref</c>/<c>out</c>/<c>in</c> parameters, and
/// <c>params</c> arrays are not allowed.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class KernelMethodAttribute : Attribute;

/// <summary>
/// Marks a class as a <b>server extension</b>: a batch operation the plugin ships as verified IR and
/// the server runs request/response in a single roundtrip, so a loop over many entities executes
/// server-side (calling the host's existing bindings) instead of one network call per entity. The
/// generator lowers the class's single public batch method — its body may use locals, a <c>foreach</c>
/// over a list parameter, host bindings via <c>ctx.Host&lt;T&gt;()</c> or constructor-injected service
/// fields, and may build and return complex objects (records/DTOs) and lists of them. For example:
/// <code>
/// [ServerExtension("monster-killer", typeof(IMonsterKillerService))]
/// public sealed partial class MonsterKillerKernel
/// {
///     private readonly IGameWorld _world;
///     public MonsterKillerKernel(IGameWorld world) =&gt; _world = world;
///
///     public List&lt;KillResult&gt; KillMonsters(List&lt;int&gt; monsterIds, HookContext ctx)
///     {
///         var results = new List&lt;KillResult&gt;();
///         foreach (var id in monsterIds)
///             results.Add(new KillResult(id, _world.Kill(id)));
///         return results;
///     }
/// }
/// public readonly record struct KillResult(int MonsterId, bool Success);
/// </code>
/// The trailing <see cref="HookContext"/> parameter is the lowering marker for host bindings (as in a
/// kernel's <c>Handle</c>) and is not part of the wire signature. Parameters, return type, and DTO
/// fields must use the supported scalar types, lists, or nested DTOs. Supplying the optional service
/// interface type lets the analyzer emit a source-generated plugin-side client that marshals directly to
/// compact server-extension value bytes instead of using a reflection proxy.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ServerExtensionAttribute : Attribute
{
    public ServerExtensionAttribute(string id) => Id = id;

    public ServerExtensionAttribute(string id, Type serviceType)
    {
        Id = id;
        ServiceType = serviceType;
    }

    public ServerExtensionAttribute(Type grafts, string? id = null)
    {
        Grafts = grafts;
        Id = id;
    }

    public string? Id { get; }

    public Type? ServiceType { get; }

    public Type? Grafts { get; }
}

/// <summary>
/// Requests a generated C# 14 extension property on <paramref name="receiverType"/> that resolves the
/// source-generated server extension client for this service. The receiver type must expose a
/// <c>ServerExtensions</c> property whose value can invoke server extensions and resolve the installed
/// service id.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ServerExtensionClientAttribute(Type receiverType, string? name = null) : Attribute
{
    public Type ReceiverType { get; } = receiverType;

    public string? Name { get; } = name;
}

/// <summary>
/// Requests a generated C# 14 extension method on <paramref name="receiverType"/> that forwards to the
/// source-generated server extension client. When <paramref name="name"/> is omitted, the kernel method name
/// is used; supply a custom name to make the receiver's domain API read naturally or avoid conflicts.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ServerExtensionMethodAttribute(Type? receiverType = null, string? name = null) : Attribute
{
    public Type? ReceiverType { get; } = receiverType;

    public string? Name { get; } = name;
}

/// <summary>
/// Requests a generated client-side registration accumulator for a control type. The generated accumulator
/// queues calls to <paramref name="methodName"/> and flushes them in order when the plugin builder starts.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GeneratePluginRegistrationAccumulatorAttribute(
    string accumulatorName,
    string methodName) : Attribute
{
    public string AccumulatorName { get; } = accumulatorName;

    public string MethodName { get; } = methodName;
}

/// <summary>
/// Requests a generated root registration accumulator that exposes child accumulators for annotated child
/// control properties.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GeneratePluginRegistrationRootAccumulatorAttribute(string accumulatorName) : Attribute
{
    public string AccumulatorName { get; } = accumulatorName;
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

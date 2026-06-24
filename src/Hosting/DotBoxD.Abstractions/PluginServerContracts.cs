namespace DotBoxD.Abstractions;

/// <summary>
/// Lifecycle and anonymous server-side invocation surface mixed into a generated plugin facade.
/// </summary>
public interface IPluginServer<TWorld>
    where TWorld : class
{
    ValueTask StartAsync(CancellationToken cancellationToken = default);

    ValueTask RunAsync(CancellationToken cancellationToken = default);

    ValueTask<TReturn> InvokeAsync<TReturn>(Func<TWorld, ValueTask<TReturn>> lambda);

    ValueTask<TReturn> InvokeAsync<TCaptures, TReturn>(
        TCaptures captures,
        RemoteServerInvocation<TWorld, TCaptures, TReturn> lambda)
        where TCaptures : class;

    ValueTask HoldUntilShutdownAsync(CancellationToken cancellationToken = default);
}

/// <summary>Lambda shape for the explicit capture-bag invoke overload on <see cref="IPluginServer{TWorld}"/>.</summary>
public delegate ValueTask<TReturn> RemoteServerInvocation<TWorld, TCaptures, TReturn>(
    TWorld world,
    TCaptures captures);

/// <summary>
/// A domain control that can host plugin-owned server extensions.
/// </summary>
public interface IExtensibleControl
{
    IServerExtensionClientRegistry ServerExtensions
        => throw ControlPlaneOnly();

    ValueTask<string> Extend<TService, TKernel>()
        where TService : class
        where TKernel : class
        => throw ControlPlaneOnly();

    protected static NotSupportedException ControlPlaneOnly()
        => new("Install verbs run on the generated plugin facade, not on the domain RPC service.");
}

/// <summary>The root plugin-service control verbs exposed by a generated plugin facade.</summary>
public interface IServiceControl : IExtensibleControl
{
    ValueTask<string> Replace<TService, TKernel>()
        where TService : class
        where TKernel : class, TService
        => throw ControlPlaneOnly();

    ILiveSettingsHandle<TKernel> Get<TKernel>()
        where TKernel : class, new()
        => throw ControlPlaneOnly();
}

/// <summary>Strongly typed live-settings tuner for an installed kernel.</summary>
public interface ILiveSettingsHandle<TKernel>
    where TKernel : class, new()
{
    ILiveSettingsHandle<TKernel> Set<TValue>(
        System.Linq.Expressions.Expression<Func<TKernel, TValue>> member,
        TValue value);

    ValueTask ApplyAsync(bool atomic = false);

    ValueTask SetValuesAsync(Action<TKernel> set, bool atomic = false);
}

/// <summary>
/// Requests a generated plugin facade and builder for the annotated partial class.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GeneratePluginServerAttribute : Attribute
{
    /// <summary>The server-authored context type augmented by the generator and used by parameterless hooks.</summary>
    public Type? Context { get; set; }

    /// <summary>
    /// Optional static factory method name on <see cref="Context"/> with signature
    /// <c>TContext Factory(HookContext raw)</c>.
    /// </summary>
    public string? ContextFactory { get; set; }
}

/// <summary>Marks a server-authored context helper as native-only and unavailable to lowered IR.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
public sealed class LocalAttribute : Attribute;

/// <summary>Analyzer-visible generated IR for server-authored SDK context <c>[KernelMethod]</c> helpers.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class GeneratedKernelMethodDescriptorAttribute(
    int version,
    Type contextType,
    string methodMetadataName,
    string normalizedSignature,
    string descriptorHash,
    string descriptorPayload)
    : Attribute
{
    public int Version { get; } = version;

    public Type ContextType { get; } = contextType;

    public string MethodMetadataName { get; } = methodMetadataName;

    public string NormalizedSignature { get; } = normalizedSignature;

    public string DescriptorHash { get; } = descriptorHash;

    public string DescriptorPayload { get; } = descriptorPayload;
}

/// <summary>
/// Identifies a generated plugin-server hook or subscription registry and the context type its parameterless
/// <c>On&lt;TEvent&gt;</c> method uses.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GeneratedPluginServerRegistryAttribute(
    GeneratedPluginServerRegistryKind kind,
    Type serverType,
    Type contextType)
    : Attribute
{
    public GeneratedPluginServerRegistryKind Kind { get; } = kind;

    public Type ServerType { get; } = serverType;

    public Type ContextType { get; } = contextType;
}

public enum GeneratedPluginServerRegistryKind
{
    Hook,
    Subscription,
}

/// <summary>Explicit host-service effects for analyzer-visible auto bindings.</summary>
[Flags]
public enum HostBindingEffect
{
    None = 0,
    HostStateRead = 1,
    HostStateWrite = 2,
    Allocates = 4
}

/// <summary>
/// Declares the capability and host-state effects required by an analyzer-visible host binding contract.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class HostCapabilityAttribute(string capability, HostBindingEffect effects) : Attribute
{
    public string Capability { get; } = capability;

    public HostBindingEffect Effects { get; } = effects;
}

/// <summary>Wire client used by generated server-extension proxies.</summary>
public interface IServerExtensionWireClient
{
    ValueTask<byte[]> InvokeServerExtensionAsync(
        string pluginId,
        byte[] arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>Registry used by generated domain controls to resolve server-extension clients.</summary>
public interface IServerExtensionClientRegistry : IServerExtensionWireClient
{
    string PluginId<TService>()
        where TService : class;
}

public interface IServerExtensionClientAccessor
{
    IServerExtensionClientRegistry ServerExtensions { get; }
}

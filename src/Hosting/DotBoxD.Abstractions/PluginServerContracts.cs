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
public sealed class GeneratePluginServerAttribute : Attribute;

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

/// <summary>
/// Declares the capability required by an implementation-backed host binding.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class HostCapabilityAttribute(string capability) : Attribute
{
    public string Capability { get; } = capability;
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

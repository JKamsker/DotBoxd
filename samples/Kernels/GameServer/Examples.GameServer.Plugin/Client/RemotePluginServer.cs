using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Game.Plugin.Client;

using System.Globalization;
using System.Reflection;

/// <summary>
/// Example-local facade that gives the plugin a server-shaped surface
/// (<c>server.Kernels.Register&lt;TService, TKernel&gt;()</c>,
/// <c>server.Kernels.Get&lt;TKernel&gt;().SetValuesAsync(..)</c>) while forwarding over the ordinary
/// <see cref="IGamePluginControlService"/> IPC contract. It resolves each kernel's analyzer-generated
/// package by type (via <see cref="KernelPackageRegistry"/>) and ships it as verified IR; generated
/// kernel RPC clients send compact binary IR through <see cref="RemoteKernelRpcControl"/>.
/// </summary>
internal sealed class RemotePluginServer : IAsyncDisposable
{
    private const string NotStartedMessage = "Call StartAsync() before using the server.";
    private readonly Func<CancellationToken, ValueTask<RemotePluginConnection>>? _connectionFactory;
    private readonly IReadOnlyList<Func<RemoteKernelControl, ValueTask>> _kernelSetup;
    private readonly IReadOnlyList<Func<RemoteKernelRpcControl, ValueTask>> _rpcSetup;
    private IGamePluginControlService? _control;
    private IAsyncDisposable? _ownedConnection;
    private RemoteKernelControl? _kernels;
    private RemoteKernelRpcControl? _kernelRpc;
    private RemoteWorldControl? _world;
    private bool _started;
    private bool _disposed;

    public RemotePluginServer(IGamePluginControlService control)
    {
        ArgumentNullException.ThrowIfNull(control);
        _kernelSetup = [];
        _rpcSetup = [];
        InitializeControls(control);
        _started = true;
    }

    internal RemotePluginServer(
        Func<CancellationToken, ValueTask<RemotePluginConnection>> connectionFactory,
        IReadOnlyList<Func<RemoteKernelControl, ValueTask>> kernelSetup,
        IReadOnlyList<Func<RemoteKernelRpcControl, ValueTask>> rpcSetup)
    {
        _connectionFactory = connectionFactory;
        _kernelSetup = kernelSetup.ToArray();
        _rpcSetup = rpcSetup.ToArray();
    }

    public RemoteKernelControl Kernels => _started ? _kernels! : throw new InvalidOperationException(NotStartedMessage);

    public RemoteKernelRpcControl KernelRpc => _started ? _kernelRpc! : throw new InvalidOperationException(NotStartedMessage);

    public RemoteWorldControl World => _started ? _world! : throw new InvalidOperationException(NotStartedMessage);

    /// <summary>
    /// Connects if needed, installs builder-queued kernels in declaration order, and enables the typed
    /// server surface. The public constructor path is already started and returns immediately.
    /// </summary>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_started)
        {
            return;
        }

        if (_connectionFactory is null)
        {
            throw new InvalidOperationException(NotStartedMessage);
        }

        var connection = await _connectionFactory(cancellationToken).ConfigureAwait(false);
        _ownedConnection = connection.OwnedConnection;
        InitializeControls(connection.Control);

        foreach (var setup in _kernelSetup)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await setup(_kernels!).ConfigureAwait(false);
        }

        foreach (var setup in _rpcSetup)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await setup(_kernelRpc!).ConfigureAwait(false);
        }

        _started = true;
    }

    public async ValueTask RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_started)
        {
            await StartAsync(cancellationToken).ConfigureAwait(false);
        }

        await HoldUntilShutdownAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Holds the connection open until the server completes its with-plugin phase.</summary>
    public ValueTask HoldUntilShutdownAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_started)
        {
            throw new InvalidOperationException(NotStartedMessage);
        }

        return _control!.HoldUntilShutdownAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownedConnection is not null)
        {
            await _ownedConnection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void InitializeControls(IGamePluginControlService control)
    {
        _control = control;
        _kernels = new RemoteKernelControl(control);
        _kernelRpc = new RemoteKernelRpcControl(control);
        _world = new RemoteWorldControl(control, _kernelRpc);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RemotePluginServer));
        }
    }
}


internal sealed class RemoteKernelRpcControl : IKernelRpcClientRegistry
{
    private readonly IGamePluginControlService _control;
    private readonly Dictionary<Type, string> _services = new();

    public RemoteKernelRpcControl(IGamePluginControlService control) => _control = control;

    public async ValueTask<string> Register<TService, TKernel>()
        where TService : class
        where TKernel : class
    {
        var json = PluginPackageJsonSerializer.Export(KernelPackageRegistry.Resolve<TKernel>());
        var pluginId = await _control.InstallKernelRpcAsync(json).ConfigureAwait(false);
        _services[typeof(TService)] = pluginId;
        return pluginId;
    }

    public string PluginId<TService>()
        where TService : class
        => _services.TryGetValue(typeof(TService), out var pluginId)
            ? pluginId
            : throw new InvalidOperationException(
                $"Kernel RPC service '{typeof(TService).FullName}' has not been registered.");

    public ValueTask<byte[]> InvokeKernelRpcAsync(
        string pluginId,
        byte[] arguments,
        CancellationToken cancellationToken = default)
        => _control.InvokeKernelRpcAsync(pluginId, arguments, cancellationToken);
}

internal sealed class RemoteWorldControl
{
    public RemoteWorldControl(IGamePluginControlService control, IKernelRpcClientRegistry kernelRpc)
    {
        Monsters = new RemoteMonsterControl(control, kernelRpc);
        Entities = new RemoteEntityControl(control);
    }

    public RemoteMonsterControl Monsters { get; }

    public RemoteEntityControl Entities { get; }
}

internal sealed class RemoteMonsterControl
{
    private readonly IGamePluginControlService _control;

    public RemoteMonsterControl(IGamePluginControlService control, IKernelRpcClientRegistry kernelRpc)
    {
        _control = control;
        KernelRpc = kernelRpc;
    }

    internal IKernelRpcClientRegistry KernelRpc { get; }

    public ValueTask<bool> KillAsync(string monsterId)
        => _control.KillMonsterAsync(monsterId);

    public ValueTask<bool> IsMonsterAsync(string entityId)
        => _control.IsMonsterAsync(entityId);
}

internal sealed class RemoteEntityControl
{
    private readonly IGamePluginControlService _control;

    public RemoteEntityControl(IGamePluginControlService control) => _control = control;

    public ValueTask<int> GetHealthAsync(string entityId)
        => _control.GetEntityHealthAsync(entityId);

    public ValueTask<int> GetLevelAsync(string entityId)
        => _control.GetEntityLevelAsync(entityId);

    public ValueTask<int> GetPositionAsync(string entityId)
        => _control.GetEntityPositionAsync(entityId);
}

internal sealed class RemoteKernelHandle<TKernel> where TKernel : class, new()
{
    private readonly IGamePluginControlService _control;
    private readonly string _pluginId;

    public RemoteKernelHandle(IGamePluginControlService control, string pluginId)
    {
        _control = control;
        _pluginId = pluginId;
    }

    /// <summary>
    /// Sets live setting values from a typed lambda. The lambda mutates a local draft; the resulting
    /// <c>[LiveSetting]</c> values are shipped over IPC. (For read-modify-write against live server
    /// state, do it server-side under the kernel's execution gate.)
    /// </summary>
    public ValueTask SetValuesAsync(Action<TKernel> set, bool atomic = false)
    {
        ArgumentNullException.ThrowIfNull(set);
        var draft = new TKernel();
        set(draft);

        var updates = typeof(TKernel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod is not null && p.GetCustomAttribute<LiveSettingAttribute>() is not null)
            .Select(p => new LiveSettingUpdate(
                p.Name,
                Convert.ToString(p.GetValue(draft), CultureInfo.InvariantCulture) ?? string.Empty))
            .ToArray();

        return _control.UpdateSettingsAsync(_pluginId, updates, atomic);
    }
}

using System.Globalization;
using System.Reflection;
using DotBoxD.Kernels.Game.Server.Abstractions;
using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Game.Plugin.Client;

internal sealed class RemotePluginServer : IDisposable, IAsyncDisposable
{
    private const string NotStartedMessage = "Call StartAsync() before using the server.";
    private readonly Func<CancellationToken, ValueTask<RemotePluginConnection>>? _connectionFactory;
    private readonly IReadOnlyList<Func<RemoteServiceControl, ValueTask>> _serviceSetup;
    private readonly IReadOnlyList<Func<RemoteWorldControl, ValueTask>> _worldSetup;
    private IGamePluginControlService? _control;
    private IAsyncDisposable? _ownedConnection;
    private RemoteServiceControl? _services;
    private RemoteWorldControl? _world;
    private bool _started;
    private bool _disposed;

    public RemotePluginServer(IGamePluginControlService control)
    {
        ArgumentNullException.ThrowIfNull(control);
        _serviceSetup = [];
        _worldSetup = [];
        InitializeControls(control);
        _started = true;
    }

    internal RemotePluginServer(
        Func<CancellationToken, ValueTask<RemotePluginConnection>> connectionFactory,
        IReadOnlyList<Func<RemoteServiceControl, ValueTask>> serviceSetup,
        IReadOnlyList<Func<RemoteWorldControl, ValueTask>> worldSetup)
    {
        _connectionFactory = connectionFactory;
        _serviceSetup = serviceSetup.ToArray();
        _worldSetup = worldSetup.ToArray();
    }

    public RemoteServiceControl Services => _started ? _services! : throw new InvalidOperationException(NotStartedMessage);

    public RemoteWorldControl World => _started ? _world! : throw new InvalidOperationException(NotStartedMessage);

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

        foreach (var setup in _serviceSetup)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await setup(_services!).ConfigureAwait(false);
        }

        foreach (var setup in _worldSetup)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await setup(_world!).ConfigureAwait(false);
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

    public ValueTask<TReturn> InvokeAsync<TReturn>(Func<IGameWorldAccess, TReturn> lambda)
    {
        ArgumentNullException.ThrowIfNull(lambda);
        throw new InvalidOperationException(
            "Remote server InvokeAsync calls must be intercepted by the DotBoxD plugin generator.");
    }

    public ValueTask<TReturn> InvokeAsync<TCaptures, TReturn>(
        TCaptures captures,
        RemoteServerInvocation<TCaptures, TReturn> lambda)
        where TCaptures : class
    {
        ArgumentNullException.ThrowIfNull(captures);
        ArgumentNullException.ThrowIfNull(lambda);
        throw new InvalidOperationException(
            "Remote server InvokeAsync calls must be intercepted by the DotBoxD plugin generator.");
    }

    public ValueTask HoldUntilShutdownAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_started)
        {
            throw new InvalidOperationException(NotStartedMessage);
        }

        return _control!.HoldUntilShutdownAsync(cancellationToken);
    }

    public void Dispose()
        => DisposeAsync().AsTask().GetAwaiter().GetResult();

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
        _services = new RemoteServiceControl(control);
        var serverExtensions = new RemoteServerExtensionControl(control);
        _world = new RemoteWorldControl(control, serverExtensions);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RemotePluginServer));
        }
    }
}

internal sealed class RemoteServerExtensionControl : IServerExtensionClientRegistry
{
    private readonly IGamePluginControlService _control;
    private readonly Dictionary<Type, string> _services = new();

    public RemoteServerExtensionControl(IGamePluginControlService control) => _control = control;

    public async ValueTask<string> Extend<TService, TKernel>()
        where TService : class
        where TKernel : class
    {
        var json = PluginPackageJsonSerializer.Export(KernelPackageRegistry.Resolve<TKernel>());
        var pluginId = await _control.InstallServerExtensionAsync(json).ConfigureAwait(false);
        _services[typeof(TService)] = pluginId;
        return pluginId;
    }

    public string PluginId<TService>()
        where TService : class
        => _services.TryGetValue(typeof(TService), out var pluginId)
            ? pluginId
            : throw new InvalidOperationException(
                $"Server extension '{typeof(TService).FullName}' has not been registered.");

    public ValueTask<byte[]> InvokeServerExtensionAsync(
        string pluginId,
        byte[] arguments,
        CancellationToken cancellationToken = default)
        => _control.InvokeServerExtensionAsync(pluginId, arguments, cancellationToken);
}

internal sealed class RemoteWorldControl
{
    public RemoteWorldControl(IGamePluginControlService control, RemoteServerExtensionControl serverExtensions)
    {
        Monsters = new RemoteMonsterControl(control, serverExtensions);
        Entities = new RemoteEntityControl(control);
    }

    public RemoteMonsterControl Monsters { get; }

    public RemoteEntityControl Entities { get; }
}

internal sealed class RemoteMonsterControl
{
    private readonly IGamePluginControlService _control;
    private readonly RemoteServerExtensionControl _serverExtensions;

    public RemoteMonsterControl(IGamePluginControlService control, RemoteServerExtensionControl serverExtensions)
    {
        _control = control;
        _serverExtensions = serverExtensions;
    }

    internal IServerExtensionClientRegistry ServerExtensions => _serverExtensions;

    public ValueTask<string> Extend<TService, TKernel>()
        where TService : class
        where TKernel : class
        => _serverExtensions.Extend<TService, TKernel>();

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

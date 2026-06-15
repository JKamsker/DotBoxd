using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.Kernels.Game.Plugin.Client;

internal sealed class RemotePluginServerBuilder
{
    private readonly Func<CancellationToken, ValueTask<RemotePluginConnection>> _connectionFactory;
    private readonly List<Func<RemoteServiceControl, ValueTask>> _serviceSetup = [];
    private readonly List<Func<RemoteWorldControl, ValueTask>> _worldSetup = [];

    private RemotePluginServerBuilder(Func<CancellationToken, ValueTask<RemotePluginConnection>> connectionFactory)
        => _connectionFactory = connectionFactory;

    public static RemotePluginServerBuilder FromConnection(IGamePluginControlService control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return new RemotePluginServerBuilder(cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new RemotePluginConnection(control, ownedConnection: null));
        });
    }

    public static RemotePluginServerBuilder FromPipeName(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        return new RemotePluginServerBuilder(async cancellationToken =>
        {
            var connection = await RpcMessagePackIpc
                .ConnectNamedPipeAsync(pipeName, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new RemotePluginConnection(connection.Get<IGamePluginControlService>(), connection);
        });
    }

    internal static RemotePluginServerBuilder FromConnectionFactory(
        Func<CancellationToken, ValueTask<RemotePluginConnection>> connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        return new RemotePluginServerBuilder(connectionFactory);
    }

    public RemotePluginServerBuilder SetupServices(Action<ServiceRegistrationAccumulator> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _serviceSetup.Add(async services =>
        {
            var accumulator = new ServiceRegistrationAccumulator(services);
            configure(accumulator);
            await accumulator.FlushAsync().ConfigureAwait(false);
        });

        return this;
    }

    public RemotePluginServerBuilder SetupWorld(Action<WorldRegistrationAccumulator> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _worldSetup.Add(async world =>
        {
            var accumulator = new WorldRegistrationAccumulator(world);
            configure(accumulator);
            await accumulator.FlushAsync().ConfigureAwait(false);
        });

        return this;
    }

    public RemotePluginServer Build()
        => new(_connectionFactory, _serviceSetup, _worldSetup);
}

internal sealed class ServiceRegistrationAccumulator
{
    private readonly RemoteServiceControl _services;
    private readonly List<Func<ValueTask>> _registrations = [];

    public ServiceRegistrationAccumulator(RemoteServiceControl services) => _services = services;

    public ServiceRegistrationAccumulator Replace<TService, TKernel>()
        where TService : class
        where TKernel : class, TService
    {
        _registrations.Add(async () =>
        {
            _ = await _services.Replace<TService, TKernel>().ConfigureAwait(false);
        });
        return this;
    }

    internal async ValueTask FlushAsync()
    {
        foreach (var registration in _registrations)
        {
            await registration().ConfigureAwait(false);
        }
    }
}

internal sealed class WorldRegistrationAccumulator
{
    private readonly RemoteMonsterExtensionAccumulator _monsters;

    public WorldRegistrationAccumulator(RemoteWorldControl world)
        => _monsters = new RemoteMonsterExtensionAccumulator(world.Monsters);

    public RemoteMonsterExtensionAccumulator Monsters => _monsters;

    internal ValueTask FlushAsync()
        => _monsters.FlushAsync();
}

internal sealed class RemoteMonsterExtensionAccumulator
{
    private readonly RemoteMonsterControl _monsters;
    private readonly List<Func<ValueTask>> _registrations = [];

    public RemoteMonsterExtensionAccumulator(RemoteMonsterControl monsters) => _monsters = monsters;

    public RemoteMonsterExtensionAccumulator Extend<TService, TKernel>()
        where TService : class
        where TKernel : class
    {
        _registrations.Add(async () =>
        {
            _ = await _monsters.Extend<TService, TKernel>().ConfigureAwait(false);
        });
        return this;
    }

    internal async ValueTask FlushAsync()
    {
        foreach (var registration in _registrations)
        {
            await registration().ConfigureAwait(false);
        }
    }
}

internal sealed class RemotePluginConnection
{
    public RemotePluginConnection(IGamePluginControlService control, IAsyncDisposable? ownedConnection)
    {
        Control = control;
        OwnedConnection = ownedConnection;
    }

    public IGamePluginControlService Control { get; }

    public IAsyncDisposable? OwnedConnection { get; }
}

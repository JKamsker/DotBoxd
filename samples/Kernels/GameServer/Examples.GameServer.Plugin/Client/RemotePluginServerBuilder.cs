using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.Kernels.Game.Plugin.Client;

internal sealed class RemotePluginServerBuilder
{
    private readonly Func<CancellationToken, ValueTask<RemotePluginConnection>> _connectionFactory;
    private readonly List<Func<RemoteKernelControl, ValueTask>> _kernelSetup = [];
    private readonly List<Func<RemoteKernelRpcControl, ValueTask>> _rpcSetup = [];

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

    public RemotePluginServerBuilder SetupKernels(Action<KernelRegistrationAccumulator> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _kernelSetup.Add(async kernels =>
        {
            var accumulator = new KernelRegistrationAccumulator(kernels);
            configure(accumulator);
            await accumulator.FlushAsync().ConfigureAwait(false);
        });

        return this;
    }

    public RemotePluginServerBuilder SetupKernelRpc(Action<KernelRpcRegistrationAccumulator> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _rpcSetup.Add(async kernelRpc =>
        {
            var accumulator = new KernelRpcRegistrationAccumulator(kernelRpc);
            configure(accumulator);
            await accumulator.FlushAsync().ConfigureAwait(false);
        });

        return this;
    }

    public RemotePluginServer Build()
        => new(_connectionFactory, _kernelSetup, _rpcSetup);
}

internal sealed class KernelRegistrationAccumulator
{
    private readonly RemoteKernelControl _kernels;
    private readonly List<Func<ValueTask>> _registrations = [];

    public KernelRegistrationAccumulator(RemoteKernelControl kernels) => _kernels = kernels;

    public KernelRegistrationAccumulator Register<TService, TKernel>()
        where TService : class
        where TKernel : class, TService
    {
        _registrations.Add(async () =>
        {
            _ = await _kernels.Register<TService, TKernel>().ConfigureAwait(false);
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

internal sealed class KernelRpcRegistrationAccumulator
{
    private readonly RemoteKernelRpcControl _kernelRpc;
    private readonly List<Func<ValueTask>> _registrations = [];

    public KernelRpcRegistrationAccumulator(RemoteKernelRpcControl kernelRpc) => _kernelRpc = kernelRpc;

    public KernelRpcRegistrationAccumulator Register<TService, TKernel>()
        where TService : class
        where TKernel : class
    {
        _registrations.Add(async () =>
        {
            _ = await _kernelRpc.Register<TService, TKernel>().ConfigureAwait(false);
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

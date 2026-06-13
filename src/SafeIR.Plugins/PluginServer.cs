namespace SafeIR.Plugins;

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using SafeIR;
using SafeIR.Hosting;

public sealed class PluginServer : IDisposable
{
    private readonly SandboxHost _host;
    private readonly SandboxPolicy _defaultPolicy;
    private readonly ExecutionMode _executionMode;
    private int _disposed;

    private PluginServer(
        SandboxHost host,
        SandboxPolicy defaultPolicy,
        IPluginMessageSink messages,
        ExecutionMode executionMode)
    {
        _host = host;
        _defaultPolicy = defaultPolicy;
        _executionMode = executionMode;
        Events = new PluginEventAdapterRegistry();
        Kernels = new KernelRegistry();
        Hooks = new HookRegistry(messages, Events, Kernels);
    }

    public HookRegistry Hooks { get; }
    public KernelRegistry Kernels { get; }
    public PluginEventAdapterRegistry Events { get; }

    public static PluginServer Create(
        IPluginMessageSink? messages = null,
        Action<SandboxHostBuilder>? configureHost = null,
        SandboxPolicy? defaultPolicy = null,
        ExecutionMode executionMode = ExecutionMode.Auto)
    {
        if (!Enum.IsDefined(executionMode))
        {
            throw new ArgumentOutOfRangeException(nameof(executionMode));
        }

        messages ??= new InMemoryPluginMessageSink();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.AddPluginMessageBindings(messages);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
            configureHost?.Invoke(builder);
        });
        defaultPolicy ??= SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .Build();
        return new PluginServer(host, defaultPolicy, messages, executionMode);
    }

    public LiveValue<T> BindValue<T>(string name, T initialValue)
        => new(name, initialValue);

    public LiveContext<T> BindContext<T>(string name, Action<T>? initialize = null) where T : class
        => LiveContextFactory.Create(name, initialize);

    public PluginServer RegisterEventAdapter<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        Events.Register(adapter);
        return this;
    }

    public async ValueTask<InstalledKernel> InstallAsync(
        PluginPackage package,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        PluginPackageValidator.Validate(package);
        var plan = await _host.PrepareAsync(package.Module, policy ?? _defaultPolicy, cancellationToken)
            .ConfigureAwait(false);
        PluginPackageValidator.ValidatePrepared(package, plan, Events);
        var kernel = new InstalledKernel(_host, plan, package, _executionMode);
        Kernels.Add(kernel);
        return kernel;
    }

    public bool Uninstall(string pluginId)
    {
        ThrowIfDisposed();
        return Kernels.Remove(pluginId);
    }

    /// <summary>
    /// Releases the owned <see cref="SandboxHost"/> (compiled executable cache, generated load
    /// contexts, hotness state, and other host-owned execution resources) so a host that retires a
    /// plugin server (per tenant, world, test, or reload) can deterministically reclaim them through
    /// the public plugin API. After disposal the lifecycle entrypoints
    /// (<see cref="InstallAsync"/>, <see cref="Uninstall"/>) throw <see cref="ObjectDisposedException"/>.
    /// Disposal is idempotent.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _host.Dispose();
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}

public sealed class KernelRegistry : IEnumerable<InstalledKernel>
{
    private readonly object _gate = new();
    private readonly Dictionary<string, InstalledKernel> _kernels = new(StringComparer.Ordinal);

    public InstalledKernel Get(string pluginId)
    {
        lock (_gate)
        {
            return _kernels[pluginId];
        }
    }

    public TypedInstalledKernel<TState> Get<TState>(string pluginId) where TState : class
        => new(Get(pluginId));

    /// <summary>
    /// Probes installation state without throwing, letting an admin/host UI discover whether a
    /// plugin id is currently installed and read its live kernel without catching
    /// <see cref="KeyNotFoundException"/>.
    /// </summary>
    public bool TryGet(string pluginId, [MaybeNullWhen(false)] out InstalledKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        lock (_gate)
        {
            return _kernels.TryGetValue(pluginId, out kernel);
        }
    }

    /// <summary>
    /// Returns a stable snapshot of the currently installed kernels for inventory rendering. The
    /// returned list is detached from registry internals, so it is safe to enumerate while installs
    /// and uninstalls continue concurrently.
    /// </summary>
    public IReadOnlyList<InstalledKernel> Snapshot()
    {
        lock (_gate)
        {
            return _kernels.Values.ToArray();
        }
    }

    /// <summary>
    /// Enumerates the currently installed kernels over a stable snapshot, so an admin/host UI can
    /// iterate the inventory directly (for example with <c>foreach</c> or LINQ) without taking a
    /// dependency on <see cref="Snapshot"/>. Enumeration is detached from registry internals and is
    /// therefore unaffected by concurrent installs and uninstalls.
    /// </summary>
    public IEnumerator<InstalledKernel> GetEnumerator() => Snapshot().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal InstalledKernel GetByKernelType<TKernel>() where TKernel : class
    {
        var pluginId = KernelTypeMetadata.PluginId(typeof(TKernel));
        return Get(pluginId);
    }

    internal void Add(InstalledKernel kernel)
    {
        InstalledKernel? revoke = null;
        lock (_gate)
        {
            if (_kernels.TryGetValue(kernel.Manifest.PluginId, out var existing) &&
                !ReferenceEquals(existing, kernel))
            {
                revoke = existing;
            }

            _kernels[kernel.Manifest.PluginId] = kernel;
        }

        revoke?.Revoke();
    }

    internal bool Remove(string pluginId)
    {
        InstalledKernel? kernel;
        lock (_gate)
        {
            if (!_kernels.Remove(pluginId, out kernel))
            {
                return false;
            }
        }

        if (kernel is null)
        {
            return false;
        }

        kernel.Revoke();
        return true;
    }
}

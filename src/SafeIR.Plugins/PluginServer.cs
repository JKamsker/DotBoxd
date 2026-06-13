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
        Hooks = new HookRegistry(messages, Events, Kernels, InstallChainPackage);
    }

    // Synchronous installer the hook pipelines use to wire analyzer-generated chain packages at
    // setup time (the generated interceptor calls HookPipeline.UseGeneratedChain).
    private InstalledKernel InstallChainPackage(PluginPackage package)
        => InstallCoreAsync(package, policy: null, owner: null, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

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

    public ValueTask<InstalledKernel> InstallAsync(
        PluginPackage package,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
        => InstallCoreAsync(package, policy, owner: null, cancellationToken);

    /// <summary>
    /// Creates a new ownership session. Every kernel installed through the session is tagged with it
    /// as the owner, so no other session can replace or mutate it; disposing the session revokes and
    /// unregisters the kernels it owns. Use one session per untrusted connection.
    /// </summary>
    public PluginSession CreateSession()
    {
        ThrowIfDisposed();
        return new PluginSession(this);
    }

    public bool Uninstall(string pluginId)
    {
        ThrowIfDisposed();
        return Kernels.Remove(pluginId);
    }

    internal ValueTask<InstalledKernel> InstallOwnedAsync(
        PluginSession owner,
        PluginPackage package,
        SandboxPolicy? policy,
        CancellationToken cancellationToken)
        => InstallCoreAsync(package, policy, owner, cancellationToken);

    internal void UninstallOwned(PluginSession owner, string pluginId)
    {
        // The owning server may have been disposed first (it revokes all kernels in Dispose); a
        // session disposing afterward is a no-op rather than an ObjectDisposedException.
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        Kernels.RemoveOwned(owner, pluginId);
    }

    private async ValueTask<InstalledKernel> InstallCoreAsync(
        PluginPackage package,
        SandboxPolicy? policy,
        object? owner,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        PluginPackageValidator.Validate(package);
        var plan = await _host.PrepareAsync(package.Module, policy ?? _defaultPolicy, cancellationToken)
            .ConfigureAwait(false);
        PluginPackageValidator.ValidatePrepared(package, plan, Events);
        var kernel = new InstalledKernel(_host, plan, package, _executionMode, owner);
        Kernels.Add(kernel);
        return kernel;
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
            // Revoke running kernels before tearing down the host so an in-flight publish cannot call
            // into a disposed SandboxHost; revocation cancels each kernel's execution token first.
            foreach (var kernel in Kernels.Snapshot())
            {
                kernel.Revoke();
            }

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
                // Fail closed if a different session already owns this id: one plugin must not be able
                // to hijack/replace another plugin's kernel by reusing its id. A same-owner reinstall
                // (hot reload) replaces and revokes the prior incumbent; a null owner is the legacy
                // in-process path and keeps replace semantics.
                if (existing.OwnerId is not null && kernel.OwnerId is not null &&
                    !ReferenceEquals(existing.OwnerId, kernel.OwnerId))
                {
                    throw new SandboxValidationException([
                        new SandboxDiagnostic(
                            "SGP060",
                            $"plugin id '{kernel.Manifest.PluginId}' is owned by another session and cannot be replaced.")
                    ]);
                }

                revoke = existing;
            }

            _kernels[kernel.Manifest.PluginId] = kernel;
        }

        revoke?.Revoke();
    }

    /// <summary>
    /// Removes and revokes a kernel only if it is owned by <paramref name="owner"/> (or has no owner),
    /// so a session disposal never tears down another session's kernel that may have replaced this id.
    /// </summary>
    internal bool RemoveOwned(PluginSession owner, string pluginId)
    {
        InstalledKernel? kernel;
        lock (_gate)
        {
            if (!_kernels.TryGetValue(pluginId, out kernel))
            {
                return false;
            }

            if (kernel.OwnerId is not null && !ReferenceEquals(kernel.OwnerId, owner))
            {
                return false;
            }

            _kernels.Remove(pluginId);
        }

        kernel.Revoke();
        return true;
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

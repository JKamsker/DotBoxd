using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins;

using DotBoxD.Kernels;

public sealed partial class PluginServer : IDisposable
{
    private readonly Hosting.Execution.SandboxHost _host;
    private readonly SandboxPolicy _defaultPolicy;
    private readonly ExecutionMode _executionMode;
    private readonly object _poolGate = new();
    private readonly List<InstalledKernelPool> _kernelPools = [];
    private int _disposed;

    private PluginServer(
        Hosting.Execution.SandboxHost host,
        SandboxPolicy defaultPolicy,
        IPluginMessageSink messages,
        ExecutionMode executionMode,
        Action<SubscriptionDeliveryFault>? onSubscriptionFault,
        Action<ResultHookFault>? onResultHookFault)
    {
        _host = host;
        _defaultPolicy = defaultPolicy;
        _executionMode = executionMode;
        Events = new PluginEventAdapterRegistry();
        Kernels = new KernelRegistry();
        Hooks = new HookRegistry(messages, Events, Kernels, InstallChainPackage, onResultHookFault);
        Subscriptions = new SubscriptionRegistry(messages, Events, Kernels, InstallChainPackage, onSubscriptionFault);
    }

    // Synchronous installer the hook pipelines use to wire analyzer-generated chain packages at
    // setup time (the generated interceptor calls HookPipeline.UseGeneratedChain).
    private InstalledKernel InstallChainPackage(PluginPackage package)
        => InstallCoreAsync(package, policy: null, owner: null, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

    public HookRegistry Hooks { get; }
    public SubscriptionRegistry Subscriptions { get; }
    public KernelRegistry Kernels { get; }
    public PluginEventAdapterRegistry Events { get; }

    public static PluginServer Create(
        IPluginMessageSink? messages,
        Action<SandboxHostBuilder>? configureHost,
        SandboxPolicy? defaultPolicy,
        ExecutionMode executionMode,
        Action<SubscriptionDeliveryFault>? onSubscriptionFault)
        => Create(messages, configureHost, defaultPolicy, executionMode, onSubscriptionFault, onResultHookFault: null);

    public static PluginServer Create(
        IPluginMessageSink? messages = null,
        Action<SandboxHostBuilder>? configureHost = null,
        SandboxPolicy? defaultPolicy = null,
        ExecutionMode executionMode = ExecutionMode.Auto,
        Action<SubscriptionDeliveryFault>? onSubscriptionFault = null,
        Action<ResultHookFault>? onResultHookFault = null)
    {
        if (!Enum.IsDefined(executionMode))
        {
            throw new ArgumentOutOfRangeException(nameof(executionMode));
        }

        messages ??= new InMemoryPluginMessageSink();
        var host = Hosting.Execution.SandboxHost.Create(builder =>
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
        return new PluginServer(host, defaultPolicy, messages, executionMode, onSubscriptionFault, onResultHookFault);
    }

    /// <summary>
    /// Analyzes a package against this server's registered host bindings and returns the concrete
    /// capabilities its verified module requires. Use this to build a least-privilege install policy
    /// from host-trusted binding metadata instead of trusting the package manifest.
    /// </summary>
    public IReadOnlyList<string> GetRequiredCapabilities(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        ThrowIfDisposed();
        return _host.GetRequiredCapabilities(package.Module);
    }

    public LiveValue<T> BindValue<T>(string name, T initialValue)
        => new(name, initialValue);

    public LiveContext<T> BindContext<T>(string name, Action<T>? initialize = null) where T : class
        => LiveContextFactory.Create(name, initialize);

    public PluginServer RegisterEventAdapter<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        Hooks.EnsureCanRegister(adapter);
        Subscriptions.EnsureCanRegister(adapter);
        Events.Register(adapter);
        return this;
    }

    public ValueTask<InstalledKernel> InstallAsync(
        PluginPackage package,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
        => InstallCoreAsync(package, policy, owner: null, cancellationToken);

    public ValueTask<InstalledKernelPool> InstallPoolAsync(
        PluginPackage package,
        int degreeOfParallelism,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
        => InstallPoolCoreAsync(package, degreeOfParallelism, policy, cancellationToken);

    /// <summary>
    /// Installs a <b>server extension</b> package: a kernel invoked request/response (via
    /// <see cref="InstalledKernel.InvokeServerExtensionAsync"/>) rather than wired to an event. It is
    /// validated by <see cref="RpcKernelPackageValidator"/> because the manifest uses the existing
    /// <c>rpcEntrypoint</c> field and has no event subscription/contract.
    /// </summary>
    public ValueTask<InstalledKernel> InstallServerExtensionAsync(
        PluginPackage package,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
        => InstallServerExtensionCoreAsync(package, policy, owner: null, cancellationToken);

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
        var removed = Kernels.Remove(pluginId);
        if (removed is not null)
        {
            RemoveKernelReferences(removed);
            // Clear by the REMOVED kernel's manifest plugin id, not the caller's argument: Kernels.Remove also
            // resolves by install id, so a caller passing an install id would otherwise leave the server-extension
            // registration (keyed by plugin id) mapped to a now-removed kernel and hand out a stale proxy.
            ClearServerExtensionRegistrations(removed.Manifest.PluginId);
        }

        return removed is not null;
    }

    public bool UninstallPool(InstalledKernelPool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ThrowIfDisposed();
        lock (_poolGate)
        {
            if (!_kernelPools.Remove(pool))
            {
                return false;
            }
        }

        pool.Revoke();
        RemovePoolReferences(pool);
        return true;
    }

    internal ValueTask<InstalledKernel> InstallOwnedAsync(
        PluginSession owner,
        PluginPackage package,
        SandboxPolicy? policy,
        CancellationToken cancellationToken)
        => InstallCoreAsync(package, policy, owner, cancellationToken);

    internal ValueTask<InstalledKernel> InstallOwnedServerExtensionAsync(
        PluginSession owner,
        PluginPackage package,
        SandboxPolicy? policy,
        CancellationToken cancellationToken)
        => InstallServerExtensionCoreAsync(package, policy, owner, cancellationToken);

    internal bool UninstallOwned(PluginSession owner, string installId)
    {
        // The owning server may have been disposed first (it revokes all kernels in Dispose); a
        // session disposing afterward is a no-op rather than an ObjectDisposedException.
        if (Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        var removed = Kernels.RemoveOwned(owner, installId);
        if (removed is not null)
        {
            RemoveKernelReferences(removed);
            ClearServerExtensionRegistrations(removed.Manifest.PluginId);
        }

        return removed is not null;
    }

    private void RemoveKernelReferences(InstalledKernel kernel)
    {
        Hooks.RemoveKernel(kernel);
        Subscriptions.RemoveKernel(kernel);
    }

    private void RemovePoolReferences(InstalledKernelPool pool)
    {
        Hooks.RemoveKernelPool(pool);
        Subscriptions.RemoveKernelPool(pool);
    }

    /// <summary>
    /// Releases the owned <see cref="Hosting.Execution.SandboxHost"/> (compiled executable cache, generated load
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

            foreach (var pool in KernelPoolSnapshot())
            {
                pool.Revoke();
            }

            _host.Dispose();
        }
    }

    private InstalledKernelPool[] KernelPoolSnapshot()
    {
        lock (_poolGate)
        {
            return [.. _kernelPools];
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}

namespace SafeIR.Plugins;

using SafeIR;
using SafeIR.Hosting;

public sealed class PluginServer
{
    private readonly SandboxHost _host;
    private readonly SandboxPolicy _defaultPolicy;

    private PluginServer(SandboxHost host, SandboxPolicy defaultPolicy, IPluginMessageSink messages)
    {
        _host = host;
        _defaultPolicy = defaultPolicy;
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
        SandboxPolicy? defaultPolicy = null)
    {
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
            .GrantGameMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .Build();
        return new PluginServer(host, defaultPolicy, messages);
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
        PluginPackageValidator.Validate(package);
        var plan = await _host.PrepareAsync(package.Module, policy ?? _defaultPolicy, cancellationToken)
            .ConfigureAwait(false);
        var kernel = new InstalledKernel(_host, plan, package);
        Kernels.Add(kernel);
        return kernel;
    }

    public bool Uninstall(string pluginId)
        => Kernels.Remove(pluginId);
}

public sealed class KernelRegistry
{
    private readonly Dictionary<string, InstalledKernel> _kernels = new(StringComparer.Ordinal);

    public InstalledKernel Get(string pluginId) => _kernels[pluginId];

    public TypedInstalledKernel<TState> Get<TState>(string pluginId) where TState : class
        => new(Get(pluginId));

    internal InstalledKernel GetByKernelType<TKernel>() where TKernel : class
    {
        var pluginId = KernelTypeMetadata.PluginId(typeof(TKernel));
        return Get(pluginId);
    }

    internal void Add(InstalledKernel kernel)
        => _kernels[kernel.Manifest.PluginId] = kernel;

    internal bool Remove(string pluginId)
    {
        if (!_kernels.Remove(pluginId, out var kernel))
        {
            return false;
        }

        kernel.Revoke();
        return true;
    }
}

namespace SafeIR.Plugins;

using SafeIR;

public sealed record PluginManifest(
    string PluginId,
    string Contract,
    ExecutionMode Mode,
    IReadOnlyList<string> Effects,
    IReadOnlyList<LiveSettingDefinition> LiveSettings,
    IReadOnlyList<HookSubscriptionManifest> Subscriptions)
{
    private IReadOnlyList<string> _effects = PluginModelCopy.List(Effects);
    private IReadOnlyList<LiveSettingDefinition> _liveSettings = PluginModelCopy.List(LiveSettings);
    private IReadOnlyList<HookSubscriptionManifest> _subscriptions = PluginModelCopy.List(Subscriptions);
    private IReadOnlyList<string> _requiredCapabilities = [];

    public IReadOnlyList<string> Effects { get => _effects; init => _effects = PluginModelCopy.List(value); }
    public IReadOnlyList<LiveSettingDefinition> LiveSettings { get => _liveSettings; init => _liveSettings = PluginModelCopy.List(value); }
    public IReadOnlyList<HookSubscriptionManifest> Subscriptions { get => _subscriptions; init => _subscriptions = PluginModelCopy.List(value); }

    /// <summary>
    /// Capabilities the verified IR requires — derived by the analyzer from what the kernel actually
    /// touches (a host-message send, a <c>[HostBinding]</c> call, a <c>[Capability]</c>-gated event
    /// property), never self-asserted for trust. Declarative: the host gates installs through binding
    /// capabilities and policy grants. Optional, defaults to empty; set via object initializer.
    /// </summary>
    public IReadOnlyList<string> RequiredCapabilities
    {
        get => _requiredCapabilities;
        init => _requiredCapabilities = PluginModelCopy.List(value ?? []);
    }
}

public sealed record LiveSettingDefinition(
    string Name,
    string Type,
    object? DefaultValue,
    object? Min = null,
    object? Max = null);

public sealed record HookSubscriptionManifest(string Event, string Kernel);

public sealed record KernelEntrypoints(string ShouldHandle, string Handle);

public sealed record PluginPackage(
    PluginManifest Manifest,
    SandboxModule Module,
    KernelEntrypoints Entrypoints)
{
    public static PluginPackage Create(
        PluginManifest manifest,
        SandboxModule module,
        KernelEntrypoints? entrypoints = null)
        => new(
            manifest,
            module,
            entrypoints ?? new KernelEntrypoints(
                PluginManifestNames.Entrypoints.ShouldHandle,
                PluginManifestNames.Entrypoints.Handle));
}

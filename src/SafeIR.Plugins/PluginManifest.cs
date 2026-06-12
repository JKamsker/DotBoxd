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

    public IReadOnlyList<string> Effects { get => _effects; init => _effects = PluginModelCopy.List(value); }
    public IReadOnlyList<LiveSettingDefinition> LiveSettings { get => _liveSettings; init => _liveSettings = PluginModelCopy.List(value); }
    public IReadOnlyList<HookSubscriptionManifest> Subscriptions { get => _subscriptions; init => _subscriptions = PluginModelCopy.List(value); }
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
        => new(manifest, module, entrypoints ?? new KernelEntrypoints("ShouldHandle", "Handle"));
}

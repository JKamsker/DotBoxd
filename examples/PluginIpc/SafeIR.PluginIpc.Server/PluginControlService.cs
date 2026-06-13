namespace SafeIR.PluginIpc.Server;

using System.Globalization;
using SafeIR.PluginIpc.Shared;
using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.PluginLocal;
using SafeIR.Plugins;

public sealed class PluginControlService : IPluginControlService
{
    private readonly InMemoryPluginMessageSink _messages;
    private readonly PluginServer _server;
    private readonly InstalledKernel _kernel;

    private PluginControlService(
        InMemoryPluginMessageSink messages,
        PluginServer server,
        InstalledKernel kernel)
    {
        _messages = messages;
        _server = server;
        _kernel = kernel;
    }

    public static async ValueTask<PluginControlService> CreateAsync()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(messages, defaultPolicy: PluginPolicy());
        server.RegisterEventAdapter(DamageEventAdapter.Instance);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create()).ConfigureAwait(false);
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
        return new PluginControlService(messages, server, kernel);
    }

    private static SandboxPolicy PluginPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantGameMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .Build();

    public ValueTask<LiveSettingSnapshot[]> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = _kernel.Manifest.LiveSettings;
        var snapshots = new LiveSettingSnapshot[settings.Count];
        for (var i = 0; i < settings.Count; i++) {
            var setting = settings[i];
            snapshots[i] = new LiveSettingSnapshot(
                setting.Name,
                setting.Type,
                Convert.ToString(_kernel.Value.GetObject(setting.Name), CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return ValueTask.FromResult(snapshots);
    }

    public ValueTask SetSettingAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _kernel.ModifySettingsAsync(
                new Dictionary<string, object?> { [name] = value },
                cancellationToken: cancellationToken);
    }

    public ValueTask ModifySettingsAsync(
        LiveSettingUpdate[] settings,
        bool atomic = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = new Dictionary<string, object?>(settings.Length, StringComparer.Ordinal);
        foreach (var setting in settings) {
            values[setting.Name] = setting.Value;
        }

        return _kernel.ModifySettingsAsync(values, atomic, cancellationToken);
    }

    public async ValueTask<string[]> PublishDamageAsync(
        DamageEventRequest request,
        CancellationToken cancellationToken = default)
    {
        var start = _messages.Messages.Count;
        await _server.Hooks.PublishAsync(
                new DamageEvent(request.DamageType, request.Amount, request.TargetId),
                cancellationToken)
            .ConfigureAwait(false);
        var messages = _messages.Messages;
        var result = new string[messages.Count - start];
        for (var i = 0; i < result.Length; i++) {
            var message = messages[start + i];
            result[i] = $"{message.TargetId}: {message.Message}";
        }

        return result;
    }
}

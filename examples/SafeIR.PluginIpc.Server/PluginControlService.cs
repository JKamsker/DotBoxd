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
        var server = PluginServer.Create(messages);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create()).ConfigureAwait(false);
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
        return new PluginControlService(messages, server, kernel);
    }

    public Task<IReadOnlyList<LiveSettingSnapshot>> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshots = _kernel.Manifest.LiveSettings
            .Select(s => new LiveSettingSnapshot {
                Name = s.Name,
                Type = s.Type,
                Value = Convert.ToString(_kernel.Value.GetObject(s.Name), CultureInfo.InvariantCulture) ?? string.Empty
            })
            .ToArray();
        return Task.FromResult<IReadOnlyList<LiveSettingSnapshot>>(snapshots);
    }

    public Task SetSettingAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _kernel.Value.Set(name, value);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<string>> PublishDamageAsync(
        DamageEventRequest request,
        CancellationToken cancellationToken = default)
    {
        var start = _messages.Messages.Count;
        await _server.Hooks.PublishAsync(
                new DamageEvent(request.DamageType, request.Amount, request.TargetId),
                cancellationToken)
            .ConfigureAwait(false);
        return _messages.Messages
            .Skip(start)
            .Select(m => $"{m.TargetId}: {m.Message}")
            .ToArray();
    }
}

using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Game.Plugin.Tests;

/// <summary>
/// Records control-plane calls for the builder tests. It implements exactly the trimmed
/// <see cref="IGamePluginControlService"/> (install IR / settings / hold / world / drain) — the per-entity
/// domain calls now live on <c>IGameWorldAccess</c> and are faked via <c>FakeWorld</c> in the tests, not here.
/// </summary>
internal sealed class RecordingGamePluginControlService : IGamePluginControlService, IAsyncDisposable
{
    private readonly TaskCompletionSource _holdStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<string> Calls { get; } = [];

    public byte[] RpcResponse { get; set; } = DefaultKillResultsResponse();

    public string? LastRpcPluginId { get; private set; }

    public byte[] LastRpcArguments { get; private set; } = [];

    public int DisposeCount { get; private set; }

    public Task HoldStarted => _holdStarted.Task;

    public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var pluginId = PluginId(packageJson);
        Calls.Add("kernel:" + pluginId);
        return ValueTask.FromResult(pluginId);
    }

    public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var pluginId = PluginId(packageJson);
        Calls.Add("extension:" + pluginId);
        return ValueTask.FromResult(pluginId);
    }

    public ValueTask<byte[]> InvokeServerExtensionAsync(
        string pluginId,
        byte[] arguments,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        LastRpcPluginId = pluginId;
        LastRpcArguments = arguments;
        Calls.Add("invoke:" + pluginId);
        return ValueTask.FromResult(RpcResponse);
    }

    public ValueTask UpdateSettingsAsync(
        string pluginId,
        LiveSettingUpdate[] updates,
        bool atomic = false,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Calls.Add("settings:" + pluginId);
        return ValueTask.CompletedTask;
    }

    public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Calls.Add("hold");
        _holdStarted.TrySetResult();
        return new ValueTask(_shutdown.Task.WaitAsync(ct));
    }

    public ValueTask<WorldSnapshot> GetWorldAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new WorldSnapshot([], tick: 0));
    }

    public ValueTask<string[]> DrainEffectsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Array.Empty<string>());
    }

    public void SignalShutdown() => _shutdown.TrySetResult();

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }

    private static string PluginId(string packageJson)
        => PluginPackageJsonSerializer.Import(packageJson).Manifest.PluginId;

    private static byte[] DefaultKillResultsResponse()
        => KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.List(
        [
            KernelRpcValue.Record(
            [
                KernelRpcValue.String("monster-3"),
                KernelRpcValue.Bool(true),
                KernelRpcValue.Int32(8),
                KernelRpcValue.Int32(4),
                KernelRpcValue.Int32(42),
                KernelRpcValue.Bool(true)
            ]),
            KernelRpcValue.Record(
            [
                KernelRpcValue.String("monster-4"),
                KernelRpcValue.Bool(true),
                KernelRpcValue.Int32(7),
                KernelRpcValue.Int32(9),
                KernelRpcValue.Int32(0),
                KernelRpcValue.Bool(false)
            ])
        ]));
}

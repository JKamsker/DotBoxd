using DotBoxD.Kernels.Game.Client.Rendering;
using DotBoxD.Kernels.Game.Client.Sandbox;
using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;

namespace DotBoxD.Kernels.Game.Plugin.Tests.ClientHost;

public sealed class ClientSandboxPolicyTests
{
    [Fact]
    public void ClientPolicy_grants_client_capabilities_without_world_gold_access()
    {
        var policy = ClientPolicy.ForKernel(
            ["dotboxd.runtime.async", "game.client.server.call", "game.client.ui.write"]);

        Assert.True(policy.GrantsCapability("dotboxd.runtime.async"));
        Assert.True(policy.GrantsCapability("game.client.server.call"));
        Assert.True(policy.GrantsCapability("game.client.ui.write"));
        Assert.False(policy.GrantsCapability("game.world.gold.write.grant"));
        Assert.False(policy.GrantsCapability("game.world.gold.read.balance"));
    }

    [Fact]
    public async Task ClientRelay_denies_operation_not_in_client_allow_list_before_server_call()
    {
        var control = new RecordingClientControl();
        var access = new GameClientAccess(new ConsoleHudRenderer(), control);

        var receipt = await access.Server.CallAsync("gold.grant", "monster-1");

        Assert.Equal("denied:client-operation", receipt);
        Assert.Empty(control.Calls);
    }

    [Fact]
    public async Task ClientRelay_forwards_allowed_bounty_operation_to_server_control_plane()
    {
        var control = new RecordingClientControl();
        var access = new GameClientAccess(new ConsoleHudRenderer(), control);

        var receipt = await access.Server.CallAsync("bounty.claim", "monster-1");

        Assert.Equal("server:bounty.claim:monster-1", receipt);
        Assert.Equal([("bounty.claim", "monster-1")], control.Calls);
    }

    private sealed class RecordingClientControl : IGameClientControlService
    {
        public List<(string Operation, string Payload)> Calls { get; } = [];

        public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
            => ValueTask.FromResult("unused");

        public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
            => ValueTask.FromResult("unused");

        public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
            => ValueTask.FromResult("unused");

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken ct = default)
            => ValueTask.FromResult(Array.Empty<byte>());

        public ValueTask UpdateSettingsAsync(
            string pluginId,
            LiveSettingUpdate[] updates,
            bool atomic = false,
            CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask<string> CallPluginOperationAsync(
            string operation,
            string payload,
            CancellationToken ct = default)
        {
            Calls.Add((operation, payload));
            return ValueTask.FromResult("server:" + operation + ":" + payload);
        }
    }
}

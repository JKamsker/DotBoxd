using SafeIR.PluginLocal;
using SafeIR.Plugins;

namespace SafeIR.Tests;

/// <summary>
/// Covers the session-ownership model: a kernel is owned by the session that installed it, cannot be
/// hijacked or tuned by another session, and is revoked + unregistered when its session is disposed
/// (the server-side equivalent of "the plugin disconnected").
/// </summary>
public sealed class PluginOwnershipTests
{
    [Fact]
    public async Task Cross_owner_id_reuse_is_rejected_fail_closed()
    {
        using var server = PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var ownerA = server.CreateSession();
        var ownerB = server.CreateSession();
        var kernelA = await ownerA.InstallAsync(FireDamagePluginPackage.Create());

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await ownerB.InstallAsync(FireDamagePluginPackage.Create()).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP060");
        Assert.False(kernelA.IsRevoked);
        Assert.True(server.Kernels.TryGet("fire-damage", out var current));
        Assert.Same(kernelA, current);
    }

    [Fact]
    public async Task Session_dispose_revokes_and_unregisters_owned_kernels()
    {
        using var server = PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var session = server.CreateSession();
        var kernel = await session.InstallAsync(FireDamagePluginPackage.Create());
        Assert.True(server.Kernels.TryGet("fire-damage", out _));

        session.Dispose();

        Assert.True(kernel.IsRevoked);
        Assert.False(server.Kernels.TryGet("fire-damage", out _));
    }

    [Fact]
    public async Task Same_owner_reinstall_replaces_and_revokes_prior()
    {
        using var server = PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var session = server.CreateSession();
        var first = await session.InstallAsync(FireDamagePluginPackage.Create());
        var second = await session.InstallAsync(FireDamagePluginPackage.Create());

        Assert.True(first.IsRevoked);
        Assert.False(second.IsRevoked);
        Assert.True(server.Kernels.TryGet("fire-damage", out var current));
        Assert.Same(second, current);
    }

    [Fact]
    public async Task UpdateSettings_rejects_a_plugin_id_the_session_does_not_own()
    {
        using var server = PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var owner = server.CreateSession();
        await owner.InstallAsync(FireDamagePluginPackage.Create());
        var intruder = server.CreateSession();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await intruder
                .UpdateSettingsAsync("fire-damage", new Dictionary<string, object?> { ["DamageType"] = "ice" })
                .AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP061");
    }

    [Fact]
    public async Task Disposed_session_rejects_further_installs()
    {
        using var server = PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var session = server.CreateSession();
        session.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await session.InstallAsync(FireDamagePluginPackage.Create()).AsTask());
    }

    [Fact]
    public async Task Owner_with_null_id_keeps_legacy_replace_semantics()
    {
        // Direct (sessionless) installs have no owner; reusing an id replaces + revokes, as before.
        using var server = PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var first = await server.InstallAsync(FireDamagePluginPackage.Create());
        var second = await server.InstallAsync(FireDamagePluginPackage.Create());

        Assert.True(first.IsRevoked);
        Assert.False(second.IsRevoked);
    }

    private static SandboxPolicy LongWallPluginPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
}

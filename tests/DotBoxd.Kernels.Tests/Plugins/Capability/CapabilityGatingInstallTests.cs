using DotBoxd.Kernels;
using DotBoxd.Kernels.PluginLocal;
using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// End-to-end capability gating at install: a kernel that needs a concrete capability
/// (FireDamageKernel uses ctx.Messages.Send → host.message.write) installs under a wildcard grant that
/// authorizes it (host.* covers host.message.write), and fails closed when no grant authorizes it.
/// </summary>
public sealed class CapabilityGatingInstallTests
{
    [Fact]
    public async Task Wildcard_grant_authorizes_a_kernel_needing_a_concrete_capability()
    {
        using var server = PluginServer.Create(defaultPolicy: WildcardHostPolicy());

        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());

        // Installed only because the wildcard host.* grant authorized host.message.write — GrantLogging
        // alone never grants it.
        Assert.Equal("fire-damage", kernel.Manifest.PluginId);
    }

    [Fact]
    public async Task Missing_capability_denies_install_fail_closed()
    {
        using var server = PluginServer.Create(defaultPolicy: LoggingOnlyPolicy());

        await Assert.ThrowsAnyAsync<Exception>(
            async () => await server.InstallAsync(FireDamagePluginPackage.Create()).AsTask());
    }

    private static SandboxPolicy WildcardHostPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .Grant("host.*", new { }, SandboxEffect.HostStateWrite | SandboxEffect.Audit)
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private static SandboxPolicy LoggingOnlyPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
}

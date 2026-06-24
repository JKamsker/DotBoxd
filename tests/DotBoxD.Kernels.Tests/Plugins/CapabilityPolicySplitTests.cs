using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class CapabilityPolicySplitTests
{
    [Fact]
    public void Generated_event_modules_do_not_mirror_host_required_capabilities_as_plugin_requests()
    {
        var package = FireDamagePluginPackage.Create();

        Assert.Contains(PluginMessageBindings.CapabilityId, package.Manifest.RequiredCapabilities);
        Assert.Empty(package.Module.CapabilityRequests);
    }

    [Fact]
    public void Server_required_capability_analysis_excludes_plugin_requested_capabilities()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = WithPluginRequest(FireDamagePluginPackage.Create(), "file.write");

        var required = server.GetRequiredCapabilities(package);

        Assert.Contains(PluginMessageBindings.CapabilityId, required);
        Assert.DoesNotContain("file.write", required);
    }

    [Fact]
    public void Server_required_capability_analysis_excludes_module_required_capability_metadata()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = WithRequiredCapabilityMetadata(FireDamagePluginPackage.Create(), "file.write");

        var required = server.GetRequiredCapabilities(package);

        Assert.Contains(PluginMessageBindings.CapabilityId, required);
        Assert.DoesNotContain("file.write", required);
    }

    [Fact]
    public async Task Manifest_parity_ignores_independently_granted_plugin_requests()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = WithPluginRequest(FireDamagePluginPackage.Create(), "file.write");
        var policy = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .GrantFileWrite(Path.GetTempPath(), maxBytesPerRun: 1, allowCreate: true)
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

        var kernel = await server.InstallAsync(package, policy);

        Assert.Equal(package.Manifest.PluginId, kernel.Manifest.PluginId);
    }

    private static PluginPackage WithPluginRequest(PluginPackage package, string capabilityId)
        => package with
        {
            Module = package.Module with
            {
                CapabilityRequests = [new CapabilityRequest(capabilityId, "requested by plugin")]
            }
        };

    private static PluginPackage WithRequiredCapabilityMetadata(PluginPackage package, string capabilityId)
    {
        var metadata = new Dictionary<string, string>(package.Module.Metadata, StringComparer.Ordinal)
        {
            ["requiredCapabilities"] = capabilityId
        };
        return package with { Module = package.Module with { Metadata = metadata } };
    }
}

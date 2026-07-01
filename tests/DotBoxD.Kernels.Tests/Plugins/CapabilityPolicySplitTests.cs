using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
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
    public void Server_required_capability_analysis_includes_gated_event_properties()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            GatedEventPropertyKernelSource,
            "Sample.GatedPluginPackage");

        var required = server.GetRequiredCapabilities(package);

        Assert.Contains("event.read.health", required);
        Assert.Contains(PluginMessageBindings.CapabilityId, required);
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

    [Fact]
    public async Task Install_denies_ungranted_plugin_requests_even_when_host_requirements_are_granted()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = WithPluginRequest(FireDamagePluginPackage.Create(), "file.write");
        var policy = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

        var exception = await Assert.ThrowsAsync<SandboxValidationException>(
            () => server.InstallAsync(package, policy).AsTask());

        Assert.Contains(exception.Diagnostics, diagnostic =>
            diagnostic.Code == "E-POLICY-CAP" &&
            diagnostic.Message.Contains("file.write", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_denies_missing_gated_event_property_manifest_capability()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            GatedEventPropertyKernelSource,
            "Sample.GatedPluginPackage");
        Assert.Contains("event.read.health", package.Manifest.RequiredCapabilities);
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                RequiredCapabilities = package.Manifest.RequiredCapabilities
                    .Where(capability => !string.Equals(capability, "event.read.health", StringComparison.Ordinal))
                    .ToArray()
            }
        };
        var policy = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

        var exception = await Assert.ThrowsAsync<SandboxValidationException>(
            () => server.InstallAsync(invalid, policy).AsTask());

        Assert.Contains(exception.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("event.read.health", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Server_extension_install_rejects_ungranted_event_property_manifest_capabilities()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            SimpleServerExtensionSource,
            "Sample.EchoPluginPackage");
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                RequiredCapabilities = [.. package.Manifest.RequiredCapabilities, "event.read.secret"]
            }
        };

        var exception = await Assert.ThrowsAsync<SandboxValidationException>(
            () => server.InstallServerExtensionAsync(invalid).AsTask());

        Assert.Contains(exception.Diagnostics, diagnostic =>
            diagnostic.Code == "E-POLICY-CAP" &&
            diagnostic.Message.Contains("event.read.secret", StringComparison.Ordinal));
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

    private const string GatedEventPropertyKernelSource = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed record GatedEvent(
            string TargetId,
            string Message,
            [property: Capability("event.read.health")] int Health);

        [Plugin("gated-property")]
        public sealed partial class GatedKernel : IEventKernel<GatedEvent>
        {
            public bool ShouldHandle(GatedEvent e, HookContext ctx) => e.Health > 0;

            public void Handle(GatedEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);
        }
        """;

    private const string SimpleServerExtensionSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;

        namespace Sample;

        [ServerExtension("echo")]
        public sealed partial class EchoKernel
        {
            public int Echo(HookContext ctx) => 1;
        }
        """;
}

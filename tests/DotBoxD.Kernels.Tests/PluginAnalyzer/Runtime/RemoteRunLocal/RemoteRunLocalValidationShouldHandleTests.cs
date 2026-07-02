using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class RemoteRunLocalValidationTests
{
    private static readonly SourceSpan LocalTerminalShouldHandleSpan = new(1, 1);

    [Fact]
    public async Task Local_terminal_package_with_host_write_ShouldHandle_is_rejected_at_install()
    {
        using var server = PluginServer.Create(new InMemoryPluginMessageSink(), defaultPolicy: Policy());
        var package = WithCallbackSubscriptionId(LocalTerminalHostWriteShouldHandlePackage());

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(package).AsTask());

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "DBXK031" &&
            d.Message.Contains("local-terminal", StringComparison.OrdinalIgnoreCase) &&
            d.Message.Contains("host-write", StringComparison.Ordinal));
    }

    private static PluginPackage LocalTerminalHostWriteShouldHandlePackage()
    {
        const string pluginId = "local-terminal-host-write-filter";
        var manifest = new PluginManifest(
            pluginId,
            "IEventKernel<HostWriteFilterEvent>",
            ExecutionMode.Interpreted,
            [
                nameof(SandboxEffect.Cpu),
                nameof(SandboxEffect.Alloc),
                nameof(SandboxEffect.HostStateWrite),
                nameof(SandboxEffect.Concurrency),
                nameof(SandboxEffect.Audit)
            ],
            [],
            [
                new HookSubscriptionManifest("HostWriteFilterEvent", pluginId)
                {
                    LocalTerminal = true,
                    ProjectedType = "string"
                }
            ])
        {
            RequiredCapabilities = [RuntimeCapabilityIds.Async, PluginMessageBindings.CapabilityId]
        };
        var module = new SandboxModule(
            pluginId,
            SemVersion.One,
            SemVersion.One,
            [new CapabilityRequest(PluginMessageBindings.CapabilityId, "host-write filter")],
            [HostWriteShouldHandle(), ProjectingHandle()],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kernel"] = pluginId,
                ["pluginId"] = pluginId
            });
        return PluginPackage.Create(manifest, module, new KernelEntrypoints("ShouldHandle", "Handle"));
    }

    private static SandboxFunction HostWriteShouldHandle()
        => new(
            "ShouldHandle",
            IsEntrypoint: true,
            [],
            SandboxType.Bool,
            [
                new ExpressionStatement(
                    new CallExpression(
                        PluginMessageBindings.SendBindingId,
                        [
                            new LiteralExpression(SandboxValue.FromString("player-1"), LocalTerminalShouldHandleSpan),
                            new LiteralExpression(SandboxValue.FromString("probe"), LocalTerminalShouldHandleSpan)
                        ],
                        GenericType: null,
                        LocalTerminalShouldHandleSpan),
                    LocalTerminalShouldHandleSpan),
                new ReturnStatement(
                    new LiteralExpression(SandboxValue.FromBool(true), LocalTerminalShouldHandleSpan),
                    LocalTerminalShouldHandleSpan)
            ]);

    private static SandboxFunction ProjectingHandle()
        => new(
            "Handle",
            IsEntrypoint: true,
            [],
            SandboxType.String,
            [
                new ReturnStatement(
                    new LiteralExpression(SandboxValue.FromString("projected"), LocalTerminalShouldHandleSpan),
                    LocalTerminalShouldHandleSpan)
            ]);
}

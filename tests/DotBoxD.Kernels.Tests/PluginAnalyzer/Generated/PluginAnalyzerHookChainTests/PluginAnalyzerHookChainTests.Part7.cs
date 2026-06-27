namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerHookChainTests
{
    [Fact]
    public void Remote_staged_Run_with_null_forgiving_stage_lowers()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1")!
                        .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK114");
        Assert.Contains(result.GeneratedTrees, tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Remote_staged_Run_with_null_forgiving_alias_lowers()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var staged = hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1");
                    staged!.Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK114");
        Assert.Contains(result.GeneratedTrees, tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_server_remote_staged_Run_with_null_forgiving_registry_alias_lowers()
    {
        var result = RunGenerator("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Services.Attributes;

            namespace Sample.Game
            {
                [DotBoxDService]
                public interface IGameWorld;
            }

            namespace Sample.Game.Ipc
            {
                public readonly record struct LiveSettingUpdate(string Name, string Value);

                public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
                {
                    ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask UpdateSettingsAsync(
                        string pluginId,
                        LiveSettingUpdate[] updates,
                        bool atomic = false,
                        CancellationToken ct = default);
                    ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
                }
            }

            namespace Sample.Plugin
            {
                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : Sample.Game.IGameWorld;

                public sealed partial class RemotePluginContext;
                public sealed record DamageEvent(string TargetId);

                public sealed class Usage
                {
                    public RemotePluginServer? Server { get; init; }

                    public void Configure()
                    {
                        var hooks = this.Server!.Hooks;
                        hooks!.On<DamageEvent>()
                            .Where(e => e.TargetId == "monster-1")!
                            .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
                    }
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "DBXK100" or "DBXK114");
        Assert.Contains(result.GeneratedTrees, tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
    }
}

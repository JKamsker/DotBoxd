using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncDtoConstructorSelectionRegressionTests
{
    [Fact]
    public void Return_dto_prefers_smaller_reconstructible_constructor_over_larger_unusable_match()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed class Profile
            {
                public Profile(int Code) => this.Code = Code;

                public Profile(int rank, int score) => Rank = rank;

                public int Code { get; }
                public int Rank { get; set; }
                public int Score => Code + Rank;
            }

            public static ValueTask<Profile> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return new Profile(world.GetHealth("monster-1")) { Rank = 9 };
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("@Rank =", source, StringComparison.Ordinal);
        Assert.Contains("__result.@Score", source, StringComparison.Ordinal);
        Assert.Contains("__field2", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Return_dto_rejects_partial_constructor_with_read_only_leftover_field()
    {
        var result = RunGenerator("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Kernels.Game.Plugin.Client;
            using DotBoxD.Kernels.Game.Server.Abstractions;

            namespace DotBoxD.Kernels.Game.Server.Abstractions
            {
                public sealed class Profile
                {
                    public Profile(int health) => Health = health;

                    public int Health { get; }
                    public int Rank { get; }
                }

                [DotBoxDService]
                public interface IGameWorldAccess
                {
                    [HostBinding("host.world.getProfile", "game.world.monster.read.profile", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                    Profile GetProfile(string entityId);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new InvalidOperationException("not used");
                }
            }

            namespace DotBoxD.Kernels.Game.Server.Abstractions.Ipc
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

            namespace DotBoxD.Kernels.Game.Plugin.Client
            {
                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }

            namespace Sample
            {
                public static class Usage
                {
                    public static ValueTask<Profile> Run(RemotePluginServer kernels)
                        => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                        {
                            return world.GetProfile("monster-1");
                        });
                }
            }
            """);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains(
                              "does not assign every public field",
                              StringComparison.Ordinal));
    }

    [Fact]
    public void Return_dto_uses_constructor_with_unmatched_optional_parameter()
    {
        var result = RunGeneratorAndAssertCompiles("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Kernels.Game.Plugin.Client;
            using DotBoxD.Kernels.Game.Server.Abstractions;

            namespace DotBoxD.Kernels.Game.Server.Abstractions
            {
                public sealed class Profile
                {
                    public Profile(int Health, bool normalize = true)
                    {
                        this.Health = normalize ? Health : -Health;
                    }

                    public int Health { get; }
                    public int Rank { get; set; }
                }

                [DotBoxDService]
                public interface IGameWorldAccess
                {
                    [HostBinding("host.world.getProfile", "game.world.monster.read.profile", SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead)]
                    Profile GetProfile(string entityId);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new InvalidOperationException("not used");
                }
            }

            namespace DotBoxD.Kernels.Game.Server.Abstractions.Ipc
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

            namespace DotBoxD.Kernels.Game.Plugin.Client
            {
                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }

            namespace Sample
            {
                public static class Usage
                {
                    public static ValueTask<Profile> Run(RemotePluginServer kernels)
                        => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                        {
                            return world.GetProfile("monster-1");
                        });
                }
            }
            """);
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("@normalize: true", source, StringComparison.Ordinal);
        Assert.Contains("@Rank =", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Return_dto_with_ref_like_constructor_reports_InvokeAsync_diagnostic()
    {
        var result = RunGenerator("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Kernels.Game.Plugin.Client;
            using DotBoxD.Kernels.Game.Server.Abstractions;

            namespace DotBoxD.Kernels.Game.Server.Abstractions
            {
                public sealed class Profile
                {
                    public Profile(out int Health) => Health = 0;

                    public int Health { get; }
                }

                [DotBoxDService]
                public interface IGameWorldAccess
                {
                    [HostBinding("host.world.getProfile", "game.world.monster.read.profile", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                    Profile GetProfile(string entityId);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new InvalidOperationException("not used");
                }
            }

            namespace DotBoxD.Kernels.Game.Server.Abstractions.Ipc
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

            namespace DotBoxD.Kernels.Game.Plugin.Client
            {
                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }

            namespace Sample
            {
                public static class Usage
                {
                    public static ValueTask<Profile> Run(RemotePluginServer kernels)
                        => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                        {
                            return world.GetProfile("monster-1");
                        });
                }
            }
            """);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("ref, out, or in", StringComparison.Ordinal));
    }
}

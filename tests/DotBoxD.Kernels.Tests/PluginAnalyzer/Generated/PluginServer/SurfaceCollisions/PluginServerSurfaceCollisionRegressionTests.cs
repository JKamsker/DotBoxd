using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

// Regression coverage for generated-facade member collisions that previously surfaced as raw Roslyn
// CS errors (CS0111 / CS0102) instead of the designed DBXK100 "collides with the generated facade surface".
public sealed class PluginServerSurfaceCollisionRegressionTests
{
    [Fact]
    public void World_member_named_like_a_generated_private_helper_reports_dbxk100()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace ReservedHelper.Game
            {
                [RpcService]
                public interface IGameWorldAccess
                {
                    void RequireWorld();
                    void Initialize();
                }
            }

            namespace ReservedHelper.Game.Ipc
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

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static ReservedHelper.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace ReservedHelper.Plugin
            {
                using DotBoxD.Abstractions;
                using ReservedHelper.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.Severity == DiagnosticSeverity.Error &&
                 d.GetMessage().Contains("collides with the generated facade surface", StringComparison.Ordinal));
    }

    [Fact]
    public void World_member_appearing_in_two_facade_categories_reports_dbxk100()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace CrossCategory.Game
            {
                [RpcService]
                public interface IMonsterControl;

                [RpcService]
                public interface ILeftWorld
                {
                    int Value { get; }
                }

                [RpcService]
                public interface IRightWorld
                {
                    IMonsterControl Value { get; }
                }

                [RpcService]
                public interface IGameWorldAccess : ILeftWorld, IRightWorld;
            }

            namespace CrossCategory.Game.Ipc
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

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static CrossCategory.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace CrossCategory.Plugin
            {
                using DotBoxD.Abstractions;
                using CrossCategory.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.Severity == DiagnosticSeverity.Error &&
                 d.GetMessage().Contains("more than one facade category", StringComparison.Ordinal));
    }

    // Two overloaded world methods share a name but differ by signature. ResolveMethods keeps both (it only
    // drops exact-signature duplicates), so both land in the methods bucket. The cross-category collision check
    // must treat repeated names WITHIN the methods bucket as one category — overloads are not a clash — so the
    // facade generates without DBXK100. Previously the second overload was wrongly rejected as a cross-category
    // clash because the check keyed only by name.
    [Fact]
    public void Overloaded_world_methods_with_same_name_generate_without_dbxk100()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Overloads.Game
            {
                [RpcService]
                public interface IGameWorldAccess
                {
                    int Ping();
                    int Ping(int times);
                }
            }

            namespace Overloads.Game.Ipc
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

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static Overloads.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Overloads.Plugin
            {
                using DotBoxD.Abstractions;
                using Overloads.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK100");
    }

    // Deduping method names must not suppress a genuine cross-category clash: a forwarded property and a
    // forwarded method that share a name (inherited from two different base interfaces) still emit twice as
    // CS0102 and must surface as the designed DBXK100.
    [Fact]
    public void World_property_and_method_with_same_name_reports_dbxk100()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace PropMethod.Game
            {
                [RpcService]
                public interface ILeftWorld
                {
                    int Status { get; }
                }

                [RpcService]
                public interface IRightWorld
                {
                    void Status();
                }

                [RpcService]
                public interface IGameWorldAccess : ILeftWorld, IRightWorld;
            }

            namespace PropMethod.Game.Ipc
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

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static PropMethod.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace PropMethod.Plugin
            {
                using DotBoxD.Abstractions;
                using PropMethod.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.Severity == DiagnosticSeverity.Error &&
                 d.GetMessage().Contains("more than one facade category", StringComparison.Ordinal));
    }
}

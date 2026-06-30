using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerMemberShapeRegressionTests
{
    [Fact]
    public void Generated_plugin_server_forwards_world_get_only_properties()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource("""
                    int CurrentTick { get; }
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("public int CurrentTick => RequireWorld().CurrentTick;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_wraps_nested_service_properties()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource("""
                    IMonsterControl Monsters { get; }
            """, """

                [DotBoxDService]
                public interface IMonsterControl
                {
                    IInventory Inventory { get; }
                }

                [DotBoxDService]
                public interface IInventory
                {
                    ValueTask<int> Count();
                }
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("private sealed class InventoryPluginService", generated, StringComparison.Ordinal);
        Assert.Contains(
            "public global::Regression.Game.IInventory Inventory => new InventoryPluginService(_owner, _inner.Inventory);",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_rejects_generic_forwarded_methods()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics(ServerSource("""
                    ValueTask<T> Read<T>();
            """));

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.Severity == DiagnosticSeverity.Error &&
                          diagnostic.GetMessage().Contains("must not be generic", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_plugin_server_rejects_ref_like_forwarded_parameters()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics(ServerSource("""
                    void Read(out int value);
            """));

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.Severity == DiagnosticSeverity.Error &&
                          diagnostic.GetMessage().Contains("ref, out, or in parameters", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_plugin_server_rejects_settable_service_properties()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics(ServerSource("""
                    IMonsterControl Monsters { get; set; }
            """, """

                [DotBoxDService]
                public interface IMonsterControl;
            """));

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.Severity == DiagnosticSeverity.Error &&
                          diagnostic.GetMessage().Contains("get-only instance property", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_plugin_server_preserves_forwarded_method_default_parameters()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource("""
                    ValueTask<int> ReadAsync(int max = 10, CancellationToken ct = default);
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains(
            "ReadAsync(int @max = 10, global::System.Threading.CancellationToken @ct = default)",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_preserves_char_and_decimal_default_parameters()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource("""
                    ValueTask<int> ReadAsync(char marker = 'x', decimal weight = 1.5m);
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("ReadAsync(char @marker = 'x', decimal @weight = 1.5m)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_preserves_nullable_enum_default_parameters()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource("""
                    ValueTask<int> ReadAsync(Mode? mode = Mode.Slow, CancellationToken ct = default);
            """, """

                public enum Mode
                {
                    Fast = 1,
                    Slow = 2
                }
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains(
            "ReadAsync(global::Regression.Game.Mode? @mode = ",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ReadAsync(global::Regression.Game.Mode? @mode = 2,",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_reports_unsupported_event_members()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics(ServerSource("""
                    event global::System.Action Changed;
            """));

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.Severity == DiagnosticSeverity.Error &&
                          diagnostic.GetMessage().Contains("event", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_plugin_server_deduplicates_compatible_inherited_methods()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Regression.Game
            {
                [DotBoxDService]
                public interface ILeftWorld
                {
                    ValueTask<int> PingAsync();
                }

                [DotBoxDService]
                public interface IRightWorld
                {
                    ValueTask<int> PingAsync();
                }

                [DotBoxDService]
                public interface IGameWorldAccess : ILeftWorld, IRightWorld;
            }

            namespace Regression.Game.Ipc
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
                    public static Regression.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Regression.Plugin
            {
                using DotBoxD.Abstractions;
                using Regression.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Equal(1, Count(generated, "public global::System.Threading.Tasks.ValueTask<int> PingAsync("));
    }

    [Fact]
    public void Generated_plugin_server_wraps_domain_ServerExtensions_member_without_collision()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource("""
                    IMonsterControl Monsters { get; }
            """, """

                [DotBoxDService]
                public interface IMonsterControl
                {
                    string ServerExtensions { get; }
                }
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains(
            "IServerExtensionClientAccessor.ServerExtensions => _owner;",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("public string ServerExtensions => _inner.ServerExtensions;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_supports_required_control_update_cancellation_token()
    {
        var source = ServerSource("")
            .Replace("bool atomic = false,", "bool atomic,", StringComparison.Ordinal)
            .Replace("CancellationToken ct = default);", "CancellationToken ct);", StringComparison.Ordinal);
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(source);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains(
            "UpdateSettingsAsync(_pluginId, _updates.ToArray(), atomic, default)",
            generated,
            StringComparison.Ordinal);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var index = 0; ; index += value.Length)
        {
            index = text.IndexOf(value, index, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
        }
    }

    private static string ServerSource(string worldMembers, string extraGameTypes = "")
        => $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Regression.Game
            {
                [DotBoxDService]
                public interface IGameWorldAccess
                {
            {{worldMembers}}
                }
            {{extraGameTypes}}
            }

            namespace Regression.Game.Ipc
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
                    public static Regression.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Regression.Plugin
            {
                using DotBoxD.Abstractions;
                using Regression.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """;
}

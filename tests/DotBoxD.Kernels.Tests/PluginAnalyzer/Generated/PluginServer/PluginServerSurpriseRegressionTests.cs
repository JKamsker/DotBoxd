using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerSurpriseRegressionTests
{
    [Fact]
    public void Internal_plugin_server_emits_internal_builder()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(BaseServerSource(
            serverAccessibility: "internal",
            contextAccessibility: "internal"));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("internal sealed class RemotePluginServerBuilder", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_plugin_server_rejects_internal_control_service()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics(BaseServerSource(
            controlAccessibility: "internal"));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.Severity == DiagnosticSeverity.Error &&
                 d.GetMessage().Contains("control-plane contract", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_plugin_server_escapes_keyword_members()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Keyword.Game
            {
                [DotBoxDService]
                public interface IGameWorldAccess
                {
                    IControl @event { get; }
                }

                [DotBoxDService]
                public interface IControl
                {
                    ValueTask<int> @class(string @record);
                }
            }

            namespace Keyword.Game.Ipc
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
                    public static Keyword.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Keyword.Plugin
            {
                using DotBoxD.Abstractions;
                using Keyword.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("public global::Keyword.Game.IControl @event", generated, StringComparison.Ordinal);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask<int> @class", generated, StringComparison.Ordinal);
        Assert.Contains("((global::Keyword.Game.IControl)_inner).@class(@record)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_disambiguates_case_variant_control_fields()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(BaseServerSource(worldMembers: """
                    IFoo Foo { get; }
                    IFoo foo { get; }
            """, extraGameTypes: """

                [DotBoxDService]
                public interface IFoo;
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("private FooPluginControl? _foo;", generated, StringComparison.Ordinal);
        Assert.Contains("private fooPluginControl? _foo_2;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_disambiguates_reserved_control_backing_field_names()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(BaseServerSource(worldMembers: """
                    IControl World { get; }
            """, extraGameTypes: """

                [DotBoxDService]
                public interface IControl;
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("private WorldPluginControl? _world_2;", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("private WorldPluginControl? _world;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_reports_generated_surface_collisions()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics(BaseServerSource(worldMembers: """
                    ValueTask StartAsync();
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("collides with the generated facade surface", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_plugin_server_rejects_world_object_member_collisions()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics(BaseServerSource(worldMembers: """
                    string ToString();
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("ToString", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("collides with the generated facade surface", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_plugin_server_emits_cancellable_InvokeAsync_and_indexed_setup_replay()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(BaseServerSource());

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("InvokeAsync<TReturn>", generated, StringComparison.Ordinal);
        Assert.Contains("CancellationToken cancellationToken = default", generated, StringComparison.Ordinal);
        Assert.Contains("_setupReplayIndex++", generated, StringComparison.Ordinal);
        Assert.Contains("AwaitAnonymousKernelAsync(pluginId, install, cancellationToken)", generated, StringComparison.Ordinal);
        Assert.Contains("installTask.WaitAsync(cancellationToken)", generated, StringComparison.Ordinal);
        Assert.Contains("InstallServerExtensionPackageAsync(factory(), default)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("InstallServerExtensionPackageAsync(factory(), cancellationToken)", generated, StringComparison.Ordinal);
        Assert.Contains("OperationCanceledException", generated, StringComparison.Ordinal);
        Assert.Contains("_anonymousKernels).Remove", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_without_pushdown_reference_omits_pipe_builder()
    {
        var (generated, outputCompilation) =
            PluginServerGenerationTestDriver.RunWithoutPushdownServices(BaseServerSource());

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.DoesNotContain("FromPipeName", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("DotBoxD.Pushdown.Services", generated, StringComparison.Ordinal);
        Assert.Contains("FromConnection", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_reports_user_partial_member_collisions()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics(BaseServerSource(serverMembers: """
                public ValueTask StartAsync(CancellationToken cancellationToken = default)
                    => default;
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("collides with the generated facade surface", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_plugin_server_reports_ambiguous_inherited_control_properties()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Regression.Game
            {
                [DotBoxDService]
                public interface ILeftControl;

                [DotBoxDService]
                public interface IRightControl;

                [DotBoxDService]
                public interface ILeftWorld
                {
                    ILeftControl Tools { get; }
                }

                [DotBoxDService]
                public interface IRightWorld
                {
                    IRightControl Tools { get; }
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

            namespace Regression.Plugin
            {
                using DotBoxD.Abstractions;
                using Regression.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("inherited property collision", StringComparison.Ordinal));
    }

}

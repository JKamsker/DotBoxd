using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientExtensionAccessibilityTests
{
    [Fact]
    public void Generated_extension_reports_inaccessible_receiver_type()
    {
        var diagnostics = Diagnostics("""
            public sealed class Owner
            {
                private sealed class RemoteMonsterControl
                {
                    public DotBoxD.Plugins.IServerExtensionClientRegistry ServerExtensions { get; } = null!;
                }

                [ServerExtensionClient(typeof(RemoteMonsterControl))]
                [ServerExtension("monster-killer", typeof(IMonsterKillerService))]
                public sealed partial class MonsterKillerKernel
                {
                    public int Kill(int monsterId, HookContext ctx) => monsterId;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("accessible from generated client code", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0122");
    }

    [Fact]
    public void Generated_extension_reports_inaccessible_receiver_type_argument()
    {
        var diagnostics = Diagnostics("""
            public sealed class RemoteMonsterControl<TToken>
            {
                public DotBoxD.Plugins.IServerExtensionClientRegistry ServerExtensions { get; } = null!;
            }

            public sealed class Owner
            {
                private sealed class SecretToken;

                [ServerExtensionClient(typeof(RemoteMonsterControl<SecretToken>))]
                [ServerExtension("monster-killer", typeof(IMonsterKillerService))]
                public sealed partial class MonsterKillerKernel
                {
                    public int Kill(int monsterId, HookContext ctx) => monsterId;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("accessible from generated client code", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0122");
    }

    [Fact]
    public void Generated_extension_reports_inaccessible_server_extensions_interface()
    {
        var diagnostics = Diagnostics("""
            public sealed class Owner
            {
                private interface ISecretControl
                {
                    DotBoxD.Plugins.IServerExtensionClientRegistry ServerExtensions { get; }
                }

                public sealed class RemoteMonsterControl : ISecretControl
                {
                    DotBoxD.Plugins.IServerExtensionClientRegistry ISecretControl.ServerExtensions => null!;
                }

                [ServerExtensionClient(typeof(RemoteMonsterControl))]
                [ServerExtension("monster-killer", typeof(IMonsterKillerService))]
                public sealed partial class MonsterKillerKernel
                {
                    public int Kill(int monsterId, HookContext ctx) => monsterId;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("ServerExtensions property interface", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0122");
    }

    [Fact]
    public void Generated_extension_supports_accessible_explicit_server_extensions_interface()
        => PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IRemoteMonsterControl
            {
                DotBoxD.Plugins.IServerExtensionClientRegistry ServerExtensions { get; }
            }

            public sealed class RemoteMonsterControl : IRemoteMonsterControl
            {
                DotBoxD.Plugins.IServerExtensionClientRegistry IRemoteMonsterControl.ServerExtensions => null!;
            }

            public interface IMonsterKillerService
            {
                ValueTask<int> KillAsync(int monsterId);
            }

            [ServerExtensionClient(typeof(RemoteMonsterControl))]
            [ServerExtension("monster-killer", typeof(IMonsterKillerService))]
            public sealed partial class MonsterKillerKernel
            {
                public int Kill(int monsterId, HookContext ctx)
                {
                    return monsterId;
                }
            }
            """);

    private static IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> Diagnostics(string body)
        => PluginAnalyzerGeneratedPackageFactory.Diagnostics($$"""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IMonsterKillerService
            {
                ValueTask<int> KillAsync(int monsterId);
            }

            {{body}}
            """);
}

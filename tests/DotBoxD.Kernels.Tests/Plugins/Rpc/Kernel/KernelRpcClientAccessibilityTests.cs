using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientAccessibilityTests
{
    [Fact]
    public void Generated_client_matches_internal_service_interface_accessibility()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            internal interface IEchoService
            {
                ValueTask<int> EchoAsync(int value);
            }

            [ServerExtension("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public int Echo(int value, HookContext ctx)
                {
                    return value;
                }
            }
            """);

        Assert.NotNull(assembly.GetType("Sample.EchoKernelServerExtensionClient", throwOnError: true));
    }

    [Fact]
    public void Generated_client_rejects_private_nested_service_interface()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class Container
            {
                private interface IEchoService
                {
                    ValueTask<int> EchoAsync(int value);
                }

                [ServerExtension("echo", typeof(IEchoService))]
                public sealed partial class EchoKernel
                {
                    public int Echo(int value, HookContext ctx)
                    {
                        return value;
                    }
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
    public void Generated_client_rejects_inaccessible_service_type_argument()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IEchoService<TToken>
            {
                ValueTask<int> EchoAsync(int value);
            }

            public sealed class Owner
            {
                private sealed class SecretToken;

                [ServerExtension("echo", typeof(IEchoService<SecretToken>))]
                public sealed partial class EchoKernel
                {
                    public int Echo(int value, HookContext ctx) => value;
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
    public void Generated_client_reports_existing_client_type_collision()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class EchoKernelServerExtensionClient;

            public interface IEchoService
            {
                ValueTask<int> EchoAsync(int value);
            }

            [ServerExtension("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public int Echo(int value, HookContext ctx) => value;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("EchoKernelServerExtensionClient", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0101");
    }

    [Fact]
    public void Generated_client_reports_existing_extension_type_collision()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class EchoKernelServerExtensionClientExtensions;

            [DotBoxDService]
            public interface IRemoteControl;

            public interface IEchoService
            {
                ValueTask<int> EchoAsync(int value);
            }

            [ServerExtensionClient(typeof(IRemoteControl))]
            [ServerExtension("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public int Echo(int value, HookContext ctx) => value;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("EchoKernelServerExtensionClientExtensions", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0101");
    }
}

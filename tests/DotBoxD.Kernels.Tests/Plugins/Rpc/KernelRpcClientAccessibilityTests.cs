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
}

using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcClientAccessibilityTests
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

            [KernelRpcService("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public int Echo(int value, HookContext ctx)
                {
                    return value;
                }
            }
            """);

        Assert.NotNull(assembly.GetType("Sample.EchoKernelRpcClient", throwOnError: true));
    }
}

using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcClientProxyValidationTests
{
    [Fact]
    public void Generated_client_rejects_service_interfaces_with_non_method_members()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IEchoService
            {
                int Count { get; }
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

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("exactly one method", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_client_allows_service_parameter_names_that_match_internal_locals()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IEchoService
            {
                ValueTask<int> EchoAsync(int __arguments);
            }

            [KernelRpcService("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public int Echo(int __arguments, HookContext ctx)
                {
                    return __arguments;
                }
            }
            """);

        Assert.NotNull(assembly.GetType("Sample.EchoKernelRpcClient", throwOnError: true));
    }

    [Fact]
    public void Generated_client_rejects_multidimensional_array_parameters()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IEchoService
            {
                ValueTask<int> EchoAsync(int[,] values);
            }

            [KernelRpcService("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public int Echo(int[,] values, HookContext ctx)
                {
                    return 0;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("multidimensional array", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_client_allows_jagged_array_results()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IEchoService
            {
                ValueTask<int[][]> EchoAsync(int[][] values);
            }

            [KernelRpcService("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public int[][] Echo(int[][] values, HookContext ctx)
                {
                    return values;
                }
            }
            """);

        Assert.NotNull(assembly.GetType("Sample.EchoKernelRpcClient", throwOnError: true));
    }

    [Fact]
    public void Generated_client_rejects_nullable_scalar_parameters()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IEchoService
            {
                ValueTask<int> EchoAsync(int? value);
            }

            [KernelRpcService("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public int Echo(int? value, HookContext ctx)
                {
                    return 0;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("nullable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Kernel_rpc_service_rejects_ref_parameters_without_service_interface()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [KernelRpcService("echo")]
            public sealed partial class EchoKernel
            {
                public int Echo(ref int value, HookContext ctx)
                {
                    return value;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("cannot use ref, in, or out", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_client_rejects_ref_parameters_even_when_contract_matches()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IEchoService
            {
                ValueTask<int> EchoAsync(ref int value);
            }

            [KernelRpcService("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public int Echo(ref int value, HookContext ctx) => value;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("cannot use ref, in, or out", StringComparison.Ordinal));
    }
}

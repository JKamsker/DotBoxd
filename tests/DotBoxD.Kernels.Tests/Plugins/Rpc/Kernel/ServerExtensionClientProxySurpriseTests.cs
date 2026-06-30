using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientProxySurpriseTests
{
    [Fact]
    public void Service_backed_generated_client_preserves_optional_parameter_defaults()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IEchoService
            {
                ValueTask<int> EchoAsync(int amount = 7, CancellationToken cancellationToken = default);
            }

            [ServerExtension("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public int Echo(int amount, HookContext ctx) => amount;
            }
            """);

        var client = assembly.GetType("Sample.EchoKernelServerExtensionClient", throwOnError: true)!;
        var method = client.GetMethod(
            "EchoAsync",
            BindingFlags.Public | BindingFlags.Instance,
            [typeof(int), typeof(System.Threading.CancellationToken)])!;
        var parameters = method.GetParameters();

        Assert.True(parameters[0].HasDefaultValue);
        Assert.Equal(7, parameters[0].DefaultValue);
        Assert.True(parameters[1].HasDefaultValue);
    }

    [Fact]
    public void Service_backed_generated_client_preserves_metadata_style_DateTime_defaults()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IClockService
            {
                ValueTask<DateTime> EchoAsync([Optional, DateTimeConstant(0L)] DateTime when);
            }

            [ServerExtension("clock", typeof(IClockService))]
            public sealed partial class ClockKernel
            {
                public DateTime Echo(DateTime when, HookContext ctx) => when;
            }
            """);

        var client = assembly.GetType("Sample.ClockKernelServerExtensionClient", throwOnError: true)!;
        var method = client.GetMethod(
            "EchoAsync",
            BindingFlags.Public | BindingFlags.Instance,
            [typeof(DateTime)])!;
        var when = Assert.Single(method.GetParameters());

        Assert.True(when.HasDefaultValue);
        Assert.Equal(default(DateTime), Assert.IsType<DateTime>(when.DefaultValue));
    }

    [Fact]
    public void Service_backed_generated_client_preserves_non_finite_double_defaults()
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
                ValueTask<double> EchoAsync(
                    double nan = double.NaN,
                    double positive = double.PositiveInfinity,
                    double negative = double.NegativeInfinity);
            }

            [ServerExtension("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public double Echo(double nan, double positive, double negative, HookContext ctx) => nan;
            }
            """);

        var client = assembly.GetType("Sample.EchoKernelServerExtensionClient", throwOnError: true)!;
        var defaults = DefaultValues<double>(client, "EchoAsync", 3);

        Assert.True(double.IsNaN(defaults[0]));
        Assert.Equal(double.PositiveInfinity, defaults[1]);
        Assert.Equal(double.NegativeInfinity, defaults[2]);
    }

    [Fact]
    public void Generated_client_rejects_service_null_reference_defaults()
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
                ValueTask<int> EchoAsync(string name = null!);
            }

            [ServerExtension("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public int Echo(string name, HookContext ctx)
                {
                    return 1;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("cannot default to null", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Direct_generated_client_supports_parameters_that_collide_with_generated_locals()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteControl;

            [ServerExtension(typeof(IRemoteControl), "direct-local-collision")]
            public sealed partial class EchoKernel
            {
                [ServerExtensionMethod(typeof(IRemoteControl))]
                public int Echo(int __arguments, HookContext ctx) => __arguments;
            }
            """);

        Assert.Contains(assembly.GetTypes(), type => type.FullName == "Sample.EchoPluginPackage");
    }

    [Fact]
    public void Direct_generated_client_rejects_generic_kernel_types()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteControl;

            [ServerExtension(typeof(IRemoteControl), "generic-direct")]
            public sealed partial class GenericKernel<T>
            {
                [ServerExtensionMethod(typeof(IRemoteControl))]
                public int Echo(int value, HookContext ctx) => value;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("generic", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0246");
    }

    [Fact]
    public void Server_extension_rejects_existing_generated_package_type_collision()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            public static class EchoPluginPackage;

            [ServerExtension("echo")]
            public sealed partial class EchoKernel
            {
                public int Echo(HookContext ctx) => 1;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("EchoPluginPackage", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0101");
    }

    private static T[] DefaultValues<T>(Type clientType, string methodName, int parameterCount)
    {
        var method = clientType.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Instance,
            Enumerable.Repeat(typeof(T), parameterCount).ToArray())!;
        return method.GetParameters()
            .Select(parameter => Assert.IsType<T>(parameter.DefaultValue))
            .ToArray();
    }

}

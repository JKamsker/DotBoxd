using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class ServerExtensionSurpriseRegressionTests
{
    [Fact]
    public void Id_only_ServerExtensionMethod_reports_DBXK100()
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

            [ServerExtension("id-only")]
            public sealed partial class EchoKernel
            {
                [ServerExtensionMethod(typeof(IRemoteControl))]
                public int Echo(HookContext ctx) => 1;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("requires a service-backed or receiver-grafted", StringComparison.Ordinal));
    }

    [Fact]
    public void Grafted_ServerExtensionMethod_receiver_mismatch_reports_DBXK100()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteMonster
            {
                string Id { get; }
            }

            [DotBoxDService]
            public interface IRemoteZone
            {
                string Id { get; }
            }

            [ServerExtension(typeof(IRemoteMonster), "mismatch")]
            public sealed partial class EchoKernel
            {
                [ServerExtensionMethod(typeof(IRemoteZone))]
                public int Echo(HookContext ctx) => 1;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("must match the class receiver", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_client_method_preserves_optional_default_parameters()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteControl;

            public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
            {
                public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                    => ServerExtensions = serverExtensions;

                public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
            }

            public interface IEchoService
            {
                int Echo(int amount = 7);
            }

            [ServerExtension("optional", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                [ServerExtensionMethod(typeof(IRemoteControl))]
                public int Echo(int amount, HookContext ctx) => amount;
            }

            public static class Probe
            {
                public static int Echo(RemoteControl control) => control.Echo();
            }
            """);

        var amount = GeneratedExtensionDefault(assembly, "Echo");

        Assert.Equal(7, amount.DefaultValue);
    }

    [Fact]
    public void Generated_client_method_preserves_non_finite_float_default_parameters()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteControl;

            public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
            {
                public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                    => ServerExtensions = serverExtensions;

                public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
            }

            public interface INanService
            {
                float Nan(float amount = float.NaN);
            }

            public interface IPositiveService
            {
                float Positive(float amount = float.PositiveInfinity);
            }

            public interface INegativeService
            {
                float Negative(float amount = float.NegativeInfinity);
            }

            [ServerExtension("float-nan", typeof(INanService))]
            public sealed partial class NanKernel
            {
                [ServerExtensionMethod(typeof(IRemoteControl))]
                public float Nan(float amount, HookContext ctx) => amount;
            }

            [ServerExtension("float-positive", typeof(IPositiveService))]
            public sealed partial class PositiveKernel
            {
                [ServerExtensionMethod(typeof(IRemoteControl))]
                public float Positive(float amount, HookContext ctx) => amount;
            }

            [ServerExtension("float-negative", typeof(INegativeService))]
            public sealed partial class NegativeKernel
            {
                [ServerExtensionMethod(typeof(IRemoteControl))]
                public float Negative(float amount, HookContext ctx) => amount;
            }
            """);

        Assert.True(float.IsNaN((float)GeneratedExtensionDefault(assembly, "Nan").DefaultValue!));
        Assert.Equal(float.PositiveInfinity, GeneratedExtensionDefault(assembly, "Positive").DefaultValue);
        Assert.Equal(float.NegativeInfinity, GeneratedExtensionDefault(assembly, "Negative").DefaultValue);
    }

    [Fact]
    public void List_member_other_than_Count_reports_DBXK100()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Collections.Generic;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("list-capacity")]
            public sealed partial class ListKernel
            {
                public int Capacity(List<int> items, HookContext ctx) => items.Capacity;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("list member access", StringComparison.Ordinal));
    }

    [Fact]
    public void Graft_receiver_base_interface_field_is_seeded_with_receiver_id()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            [DotBoxDService]
            public interface IEntity
            {
                string Id { get; }

                [HostBinding("host.entity.threat", "entity.read.threat", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int Threat();
            }

            [DotBoxDService]
            public interface IRemoteMonster : IEntity;

            [ServerExtension(typeof(IRemoteMonster), "threat")]
            public sealed partial class ThreatKernel
            {
                private readonly IEntity _entity;

                public ThreatKernel(IRemoteMonster entity) => _entity = entity;

                public int Read(HookContext ctx) => _entity.Threat();
            }
            """, "Sample.ThreatPluginPackage");

        var function = Assert.Single(package.Module.Functions);
        Assert.Contains(function.Parameters, parameter => parameter.Name == "__receiverId");
    }

    private static ParameterInfo GeneratedExtensionDefault(Assembly assembly, string methodName)
    {
        var receiver = assembly.GetType("Sample.RemoteControl", throwOnError: true)!;
        var method = assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Single(method =>
            {
                var parameters = method.GetParameters();
                return string.Equals(method.Name, methodName, StringComparison.Ordinal) &&
                       parameters.Length == 2 &&
                       parameters[0].ParameterType.IsAssignableFrom(receiver);
            });

        var parameter = method.GetParameters()[1];
        Assert.True(parameter.HasDefaultValue);
        return parameter;
    }
}

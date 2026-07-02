using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class ServerExtensionGeneratedDtoReaderRegressionTests
{
    [Fact]
    public void Direct_extension_generated_client_reconstructs_same_assembly_internal_dto_constructor()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteWorldControl
            {
            }

            public sealed class RemoteWorldControl : IRemoteWorldControl, IServerExtensionClientAccessor
            {
                public RemoteWorldControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                    => ServerExtensions = serverExtensions;

                public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
            }

            public sealed class KillResult
            {
                internal KillResult(int id, bool ok)
                {
                    Id = id;
                    Ok = ok;
                }

                public int Id { get; }
                public bool Ok { get; }
            }

            [ServerExtension(typeof(IRemoteWorldControl), "kill")]
            public sealed partial class KillKernel
            {
                [ServerExtensionMethod(typeof(IRemoteWorldControl))]
                public KillResult Kill(int id, HookContext ctx)
                {
                    return new KillResult(id, true);
                }
            }

            public static class Probe
            {
                public static KillResult Kill(RemoteWorldControl control, int id) => control.Kill(id);
            }
            """);
        var control = CreateControl(
            assembly,
            "kill",
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [
                KernelRpcValue.Int32(7),
                KernelRpcValue.Bool(true)
            ])));

        var result = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Kill", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, 7])!;

        var type = result.GetType();
        Assert.Equal(7, type.GetProperty("Id")!.GetValue(result));
        Assert.Equal(true, type.GetProperty("Ok")!.GetValue(result));
    }

    [Fact]
    public void Direct_extension_generated_client_skips_unreconstructible_partial_constructor()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteWorldControl
            {
            }

            public sealed class RemoteWorldControl : IRemoteWorldControl, IServerExtensionClientAccessor
            {
                public RemoteWorldControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                    => ServerExtensions = serverExtensions;

                public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
            }

            public sealed class Choice
            {
                public Choice(int id, string name)
                {
                    Id = id;
                    Name = name;
                }

                public Choice(int id, int count)
                {
                    Id = id;
                    Count = count;
                }

                public int Id { get; }
                public int Count { get; }
                public string Name { get; set; } = "";
            }

            [ServerExtension(typeof(IRemoteWorldControl), "choice")]
            public sealed partial class ChoiceKernel
            {
                [ServerExtensionMethod(typeof(IRemoteWorldControl))]
                public Choice Read(int id, HookContext ctx)
                {
                    return new Choice(id, 2) { Name = "hero" };
                }
            }

            public static class Probe
            {
                public static Choice Read(RemoteWorldControl control, int id) => control.Read(id);
            }
            """);
        var control = CreateControl(
            assembly,
            "choice",
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [
                KernelRpcValue.Int32(7),
                KernelRpcValue.Int32(2),
                KernelRpcValue.String("hero")
            ])));

        var result = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, 7])!;

        var type = result.GetType();
        Assert.Equal(7, type.GetProperty("Id")!.GetValue(result));
        Assert.Equal(2, type.GetProperty("Count")!.GetValue(result));
        Assert.Equal("hero", type.GetProperty("Name")!.GetValue(result));
    }

    [Fact]
    public void Server_extension_dto_ignores_public_property_with_private_getter()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class PublicSetterPrivateGetter
            {
                public int Id { get; set; }
                public string Secret { private get; set; } = "";
            }

            [ServerExtension("private-getter")]
            public sealed partial class PrivateGetterKernel
            {
                public int Read(PublicSetterPrivateGetter dto, HookContext ctx)
                {
                    return dto.Id;
                }
            }
            """, "Sample.PrivateGetterPluginPackage");

        var function = Assert.Single(package.Module.Functions);
        var parameter = Assert.Single(function.Parameters);
        Assert.Equal(SandboxType.Record([SandboxType.I32]), parameter.Type);
    }

    [Fact]
    public void Direct_extension_rejects_partial_constructor_with_read_only_leftover_field()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteWorldControl
            {
            }

            public sealed class ReadOnlyTail
            {
                public ReadOnlyTail(int id) => Id = id;

                public int Id { get; }
                public int Count { get; }
            }

            public interface IWorld
            {
                [HostBinding("host.tail.read", "tail.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                ReadOnlyTail ReadTail(int id);
            }

            [ServerExtension(typeof(IRemoteWorldControl), "tail")]
            public sealed partial class TailKernel
            {
                [ServerExtensionMethod(typeof(IRemoteWorldControl))]
                public ReadOnlyTail Read(int id, HookContext ctx)
                {
                    return ctx.Host<IWorld>().ReadTail(id);
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("does not assign every public field", StringComparison.Ordinal));
    }

    [Fact]
    public void Direct_extension_rejects_required_read_only_dto_without_SetsRequiredMembers()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteWorldControl
            {
            }

            public sealed class RequiredProfile
            {
                public RequiredProfile(int health) => Health = health;

                public required int Health { get; }
            }

            public interface IWorld
            {
                [HostBinding("host.profile.read", "profile.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                RequiredProfile ReadProfile(int id);
            }

            [ServerExtension(typeof(IRemoteWorldControl), "required-profile")]
            public sealed partial class RequiredProfileKernel
            {
                [ServerExtensionMethod(typeof(IRemoteWorldControl))]
                public RequiredProfile Read(int id, HookContext ctx)
                {
                    return ctx.Host<IWorld>().ReadProfile(id);
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("required", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS9035");
    }
}

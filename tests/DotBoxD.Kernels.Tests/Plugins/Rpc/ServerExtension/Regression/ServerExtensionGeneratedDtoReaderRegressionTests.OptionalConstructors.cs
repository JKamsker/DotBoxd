using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class ServerExtensionGeneratedDtoReaderRegressionTests
{
    [Fact]
    public void Direct_extension_reconstructs_a_dto_constructor_with_trailing_optional_parameter()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
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

            public sealed class Profile
            {
                public Profile(int health, bool normalize = true)
                {
                    Health = normalize ? health : -health;
                }

                public int Health { get; }
                public int Rank { get; set; }
            }

            public interface IWorld
            {
                [HostBinding("host.profile.read", "profile.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                Profile ReadProfile(int x);
            }

            [ServerExtension(typeof(IRemoteWorldControl), "optional-reader")]
            public sealed partial class ProfileKernel
            {
                [ServerExtensionMethod(typeof(IRemoteWorldControl))]
                public Profile Read(int x, HookContext ctx)
                {
                    return ctx.Host<IWorld>().ReadProfile(x);
                }
            }

            public static class Probe
            {
                public static Profile Read(RemoteWorldControl control, int x) => control.Read(x);
            }
            """);
        var control = CreateControl(
            assembly,
            "optional-reader",
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [
                KernelRpcValue.Int32(3),
                KernelRpcValue.Int32(9)
            ])));

        var profile = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, 3])!;

        var type = profile.GetType();
        Assert.Equal(3, type.GetProperty("Health")!.GetValue(profile));
        Assert.Equal(9, type.GetProperty("Rank")!.GetValue(profile));
    }
}

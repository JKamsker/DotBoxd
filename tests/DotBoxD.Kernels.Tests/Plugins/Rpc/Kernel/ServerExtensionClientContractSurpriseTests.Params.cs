using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class ServerExtensionClientContractSurpriseTests
{
    [Fact]
    public async Task Generated_client_preserves_params_service_contract()
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
            public interface IRemoteControl;

            public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
            {
                public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                    => ServerExtensions = serverExtensions;

                public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
            }

            public interface ISumService
            {
                ValueTask<int> SumAsync(params int[] values);
            }

            [ServerExtensionClient(typeof(IRemoteControl), "SumClient")]
            [ServerExtension("sum", typeof(ISumService))]
            public sealed partial class SumKernel
            {
                [ServerExtensionMethod(typeof(IRemoteControl), "Sum")]
                public int Sum(int[] values, HookContext ctx) => 0;
            }

            public static class Probe
            {
                public static ValueTask<int> ViaProperty(RemoteControl control)
                    => control.SumClient.SumAsync(1, 2, 3);

                public static ValueTask<int> ViaMethod(RemoteControl control)
                    => control.Sum(4, 5);
            }
            """);
        var registry = new RecordingRegistry(
            "sum",
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(42)));
        var controlType = assembly.GetType("Sample.RemoteControl", throwOnError: true)!;
        var control = Activator.CreateInstance(controlType, [registry])!;
        var probeType = assembly.GetType("Sample.Probe", throwOnError: true)!;

        var propertyCall = probeType.GetMethod("ViaProperty", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control])!;
        var propertyResult = await AwaitValueTaskResult(propertyCall);
        Assert.Equal(42, propertyResult);
        AssertArguments(registry.LastArguments, [1, 2, 3]);

        var methodCall = probeType.GetMethod("ViaMethod", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control])!;
        var methodResult = await AwaitValueTaskResult(methodCall);
        Assert.Equal(42, methodResult);
        AssertArguments(registry.LastArguments, [4, 5]);

        var clientMethod = assembly.GetType("Sample.SumKernelServerExtensionClient", throwOnError: true)!
            .GetMethod("SumAsync", BindingFlags.Public | BindingFlags.Instance)!;
        Assert.True(clientMethod.GetParameters()[0].IsDefined(typeof(ParamArrayAttribute), inherit: false));
    }

    private static void AssertArguments(byte[] payload, int[] expected)
    {
        var argument = Assert.Single(KernelRpcBinaryCodec.DecodeArguments(payload));
        Assert.Equal(expected, argument.Items.Select(item => item.Int32Value));
    }
}

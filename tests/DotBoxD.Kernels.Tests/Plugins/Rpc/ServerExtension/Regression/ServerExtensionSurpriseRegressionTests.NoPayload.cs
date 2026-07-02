using System.Reflection;
using System.Runtime.ExceptionServices;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class ServerExtensionSurpriseRegressionTests
{
    [Theory]
    [InlineData("void", "")]
    [InlineData("Task", "async")]
    [InlineData("ValueTask", "async")]
    public async Task Direct_extension_rejects_non_unit_no_payload_response(
        string returnType,
        string modifier)
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(
            NoPayloadSource(returnType, modifier, includeProbe: true));
        var controlType = assembly.GetType("Sample.RemoteMonsterControl", throwOnError: true)!;
        var probeType = assembly.GetType("Sample.Probe", throwOnError: true)!;
        var registry = new RecordingServerExtensionsRegistry(
            "ping",
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(7)));
        var control = Activator.CreateInstance(controlType, [registry])!;

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => InvokeNoPayloadProbeAsync(probeType, control));

        Assert.Contains("Unit", ex.Message, StringComparison.Ordinal);
    }

    private static async Task InvokeNoPayloadProbeAsync(Type probeType, object control)
    {
        try
        {
            var result = probeType.GetMethod("Ping", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [control]);
            await AwaitNoPayload(result).ConfigureAwait(false);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }
}

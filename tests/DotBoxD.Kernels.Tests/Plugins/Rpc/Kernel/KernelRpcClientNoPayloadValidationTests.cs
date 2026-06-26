using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientNoPayloadValidationTests
{
    [Fact]
    public async Task Generated_proxy_rejects_non_unit_no_payload_response()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IPingService
            {
                Task PingAsync();
            }

            [ServerExtension("ping", typeof(IPingService))]
            public sealed partial class PingKernel
            {
                public void Ping(HookContext ctx)
                {
                }
            }
            """);
        var clientType = assembly.GetType("Sample.PingKernelServerExtensionClient", throwOnError: true)!;
        var serviceType = assembly.GetType("Sample.IPingService", throwOnError: true)!;
        var client = Activator.CreateInstance(
            clientType,
            [new RecordingWireClient(KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(5))), "ping"])!;

        var task = (Task)serviceType.GetMethod("PingAsync")!.Invoke(client, [])!;
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => task);

        Assert.Contains("Unit", ex.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingWireClient(byte[] response) : DotBoxD.Plugins.IServerExtensionWireClient
    {
        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(response);
    }
}

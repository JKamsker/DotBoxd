using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcDtoCaseMatchingTests
{
    private const string CaseResultSource = """
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;

        namespace Sample;

        public sealed class CaseResult
        {
            public CaseResult(int monsterId, int MonsterId)
            {
                this.monsterId = monsterId;
                this.MonsterId = MonsterId;
            }

            public int MonsterId { get; }
            public int monsterId { get; }
        }

        public interface ICaseService
        {
            ValueTask<CaseResult> GetAsync();
        }

        [KernelRpcService("case-result", typeof(ICaseService))]
        public sealed partial class CaseKernel
        {
            public CaseResult Get(HookContext ctx)
            {
                return new CaseResult(monsterId: 20, MonsterId: 10);
            }
        }
        """;

    [Fact]
    public void FromSandboxValue_prefers_exact_case_constructor_parameter_matches()
    {
        var sandbox = SandboxValue.FromRecord(
            [SandboxValue.FromInt32(10), SandboxValue.FromInt32(20)]);

        var dto = Assert.IsType<CaseAmbiguousDto>(
            KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(CaseAmbiguousDto)));

        Assert.Equal(10, dto.MonsterId);
        Assert.Equal(20, dto.monsterId);
    }

    [Fact]
    public async Task Generated_rpc_kernel_maps_case_distinct_constructor_parameters_once()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            CaseResultSource,
            "Sample.CasePluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallRpcAsync(package);

        var result = Assert.IsType<RecordValue>(await kernel.InvokeRpcAsync([]));

        Assert.Equal(
            [SandboxValue.FromInt32(10), SandboxValue.FromInt32(20)],
            result.Fields);
    }

    [Fact]
    public async Task Generated_ipc_client_prefers_exact_case_constructor_parameter_matches()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(CaseResultSource);
        var clientType = assembly.GetType("Sample.CaseKernelRpcClient", throwOnError: true)!;
        var wireClient = new RecordingKernelRpcWireClient(CaseResultResponse());
        var create = clientType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
        var service = create.Invoke(null, [wireClient, "case-result"])!;
        var method = service.GetType().GetMethod("GetAsync", BindingFlags.Public | BindingFlags.Instance)!;

        var result = await AwaitValueTaskResult(method.Invoke(service, [])!);

        AssertGeneratedCaseResult(result!, monsterId: 20, upperMonsterId: 10);
    }

    private static byte[] CaseResultResponse()
        => KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
        [
            KernelRpcValue.Int32(10),
            KernelRpcValue.Int32(20)
        ]));

    private static async Task<object?> AwaitValueTaskResult(object valueTask)
    {
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }

    private static void AssertGeneratedCaseResult(
        object result,
        int monsterId,
        int upperMonsterId)
    {
        var type = result.GetType();
        Assert.Equal(upperMonsterId, type.GetProperty("MonsterId")!.GetValue(result));
        Assert.Equal(monsterId, type.GetProperty("monsterId")!.GetValue(result));
    }

    private sealed class CaseAmbiguousDto
    {
        public CaseAmbiguousDto(int monsterId, int MonsterId)
        {
            this.monsterId = monsterId;
            this.MonsterId = MonsterId;
        }

        public int MonsterId { get; }

        public int monsterId { get; }
    }

    private sealed class RecordingKernelRpcWireClient(byte[] response) : IKernelRpcWireClient
    {
        public ValueTask<byte[]> InvokeKernelRpcAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(response);
        }
    }
}

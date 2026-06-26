using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class KernelRpcMarshallerSurpriseTests
{
    [Fact]
    public void KernelRpcMarshaller_round_trips_dto_with_public_properties_and_fields()
    {
        var dto = new MixedPropertyFieldDto { Health = 3, Rank = 9 };

        var sandbox = KernelRpcMarshaller.ToSandboxValue(dto, typeof(MixedPropertyFieldDto));

        var record = Assert.IsType<RecordValue>(sandbox);
        Assert.Equal(
            [SandboxValue.FromInt32(3), SandboxValue.FromInt32(9), SandboxValue.FromInt32(12)],
            record.Fields);

        var fromKernel = Assert.IsType<MixedPropertyFieldDto>(
            KernelRpcMarshaller.FromKernelRpcValue(
                KernelRpcValue.Record(
                [
                    KernelRpcValue.Int32(4),
                    KernelRpcValue.Int32(8),
                    KernelRpcValue.Int32(12)
                ]),
                typeof(MixedPropertyFieldDto)));

        Assert.Equal(4, fromKernel.Health);
        Assert.Equal(8, fromKernel.Rank);
        Assert.Equal(12, fromKernel.Score);
    }

    private sealed class MixedPropertyFieldDto
    {
        public int Health { get; set; }

        public int Rank;

        public int Score => Health + Rank;
    }
}

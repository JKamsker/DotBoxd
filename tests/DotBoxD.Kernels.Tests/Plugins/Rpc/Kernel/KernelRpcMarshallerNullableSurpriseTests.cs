using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class KernelRpcMarshallerSurpriseTests
{
    [Fact]
    public void FromKernelRpcValue_rejects_absent_nullable_with_wrong_value_slot()
    {
        var value = KernelRpcValue.Record(
        [
            KernelRpcValue.Bool(false),
            KernelRpcValue.String("wrong")
        ]);

        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromKernelRpcValue(value, typeof(int?)));
    }

    [Fact]
    public void FromSandboxValue_rejects_absent_nullable_with_wrong_value_slot()
    {
        var value = SandboxValue.FromRecord(
        [
            SandboxValue.FromBool(false),
            SandboxValue.FromString("wrong")
        ]);

        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(value, typeof(int?)));
    }
}

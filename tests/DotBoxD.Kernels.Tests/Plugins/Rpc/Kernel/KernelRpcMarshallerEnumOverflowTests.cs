using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class KernelRpcMarshallerSurpriseTests
{
    [Fact]
    public void Runtime_marshaller_accepts_declared_high_bit_ulong_enum_values()
    {
        Assert.Equal(
            DefinedHugeEnum.Top,
            Assert.IsType<DefinedHugeEnum>(
                KernelRpcMarshaller.FromKernelRpcValue(KernelRpcValue.Int64(-1), typeof(DefinedHugeEnum))));
        Assert.Equal(
            DefinedHugeEnum.Top,
            Assert.IsType<DefinedHugeEnum>(
                KernelRpcMarshaller.FromSandboxValue(SandboxValue.FromInt64(-1), typeof(DefinedHugeEnum))));
        Assert.Equal(
            SandboxValue.FromInt64(-1),
            KernelRpcMarshaller.ToSandboxValue(DefinedHugeEnum.Top, typeof(DefinedHugeEnum)));
    }

    [Fact]
    public void Runtime_marshaller_rejects_undefined_negative_ulong_enum_wraps()
    {
        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromKernelRpcValue(KernelRpcValue.Int64(-1), typeof(SparseHugeEnum)));
        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(SandboxValue.FromInt64(-1), typeof(SparseHugeEnum)));
    }

    [Fact]
    public void Runtime_marshaller_rejects_undefined_high_bit_ulong_enum_before_encoding()
    {
        const ulong highBit = 1UL << 63;
        var value = (SparseHugeEnum)highBit;
        var wire = unchecked((long)highBit);

        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(SandboxValue.FromInt64(wire), typeof(SparseHugeEnum)));
        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.ToSandboxValue(value, typeof(SparseHugeEnum)));
    }

    [Fact]
    public void Runtime_marshaller_accepts_declared_high_bit_ulong_flags()
    {
        var wire = unchecked((long)((1UL << 63) | 1UL));

        Assert.Equal(
            HugeFlags.High | HugeFlags.Low,
            Assert.IsType<HugeFlags>(
                KernelRpcMarshaller.FromKernelRpcValue(KernelRpcValue.Int64(wire), typeof(HugeFlags))));
        Assert.Equal(
            HugeFlags.High | HugeFlags.Low,
            Assert.IsType<HugeFlags>(
                KernelRpcMarshaller.FromSandboxValue(SandboxValue.FromInt64(wire), typeof(HugeFlags))));
        Assert.Equal(
            SandboxValue.FromInt64(wire),
            KernelRpcMarshaller.ToSandboxValue(HugeFlags.High | HugeFlags.Low, typeof(HugeFlags)));
    }

    [Fact]
    public void Runtime_marshaller_rejects_negative_ulong_flags_with_undeclared_bits()
    {
        var wire = unchecked((long)((1UL << 63) | 2UL));

        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromKernelRpcValue(KernelRpcValue.Int64(wire), typeof(HugeFlags)));
        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(SandboxValue.FromInt64(wire), typeof(HugeFlags)));
    }

    private enum SparseHugeEnum : ulong
    {
        Zero = 0
    }

    private enum DefinedHugeEnum : ulong
    {
        Zero = 0,
        Top = ulong.MaxValue
    }

    [Flags]
    private enum HugeFlags : ulong
    {
        Low = 1UL,
        High = 1UL << 63
    }
}

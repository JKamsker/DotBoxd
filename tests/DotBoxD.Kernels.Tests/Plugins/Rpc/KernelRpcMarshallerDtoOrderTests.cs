using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcMarshallerDtoOrderTests
{
    [Fact]
    public void ToSandboxValue_rejects_null_string()
        => Assert.Throws<ArgumentNullException>(
            () => KernelRpcMarshaller.ToSandboxValue(null, typeof(string)));

    [Fact]
    public void ToSandboxValue_rejects_null_list()
        => Assert.Throws<ArgumentNullException>(
            () => KernelRpcMarshaller.ToSandboxValue(null, typeof(List<int>)));

    [Fact]
    public void ToSandboxValue_rejects_multidimensional_arrays()
        => Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.ToSandboxValue(new int[1, 1], typeof(int[,])));

    [Fact]
    public void FromSandboxValue_rejects_multidimensional_arrays()
        => Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(
                SandboxValue.FromList([SandboxValue.FromInt32(1)], SandboxType.I32),
                typeof(int[,])));

    [Fact]
    public void SandboxTypeOf_rejects_multidimensional_arrays()
        => Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.SandboxTypeOf(typeof(int[,])));

    [Fact]
    public void ToSandboxValue_rejects_nullable_scalar_types()
        => Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.ToSandboxValue(1, typeof(int?)));

    [Fact]
    public void FromSandboxValue_rejects_nullable_scalar_types()
        => Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(SandboxValue.FromInt32(1), typeof(int?)));

    [Fact]
    public void SandboxTypeOf_rejects_nullable_scalar_types()
        => Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.SandboxTypeOf(typeof(int?)));

    [Fact]
    public void ToSandboxValue_uses_property_order_when_constructor_order_differs()
    {
        var dto = new ReorderedDto(success: true, monsterId: 7);

        var sandbox = KernelRpcMarshaller.ToSandboxValue(dto, typeof(ReorderedDto));

        var record = Assert.IsType<RecordValue>(sandbox);
        Assert.Equal(
            [SandboxValue.FromInt32(7), SandboxValue.FromBool(true)],
            record.Fields);
    }

    [Fact]
    public void FromSandboxValue_constructs_dto_when_constructor_order_differs()
    {
        var sandbox = SandboxValue.FromRecord(
            [SandboxValue.FromInt32(8), SandboxValue.FromBool(false)]);

        var dto = Assert.IsType<ReorderedDto>(
            KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(ReorderedDto)));

        Assert.Equal(8, dto.MonsterId);
        Assert.False(dto.Success);
    }

    [Fact]
    public void FromSandboxValue_skips_dto_constructors_with_matching_names_but_wrong_types()
    {
        var sandbox = SandboxValue.FromRecord(
            [SandboxValue.FromInt32(9), SandboxValue.FromBool(true)]);

        var dto = Assert.IsType<OverloadedDto>(
            KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(OverloadedDto)));

        Assert.Equal(9, dto.MonsterId);
        Assert.True(dto.Success);
    }

    private sealed class ReorderedDto
    {
        public ReorderedDto(bool success, int monsterId)
        {
            Success = success;
            MonsterId = monsterId;
        }

        public int MonsterId { get; }

        public bool Success { get; }
    }

    private sealed class OverloadedDto
    {
        public OverloadedDto(string monsterId, bool success)
        {
            MonsterId = int.Parse(monsterId, System.Globalization.CultureInfo.InvariantCulture);
            Success = success;
        }

        public OverloadedDto(int monsterId, bool success)
        {
            MonsterId = monsterId;
            Success = success;
        }

        public int MonsterId { get; }

        public bool Success { get; }
    }
}

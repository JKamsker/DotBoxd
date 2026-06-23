using DotBoxD.Kernels.Sandbox;
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
    public void ToSandboxValue_encodes_nullable_scalar_values_as_has_value_records()
    {
        var present = Assert.IsType<RecordValue>(KernelRpcMarshaller.ToSandboxValue(7, typeof(int?)));
        Assert.Equal([SandboxValue.FromBool(true), SandboxValue.FromInt32(7)], present.Fields);

        var absent = Assert.IsType<RecordValue>(KernelRpcMarshaller.ToSandboxValue(null, typeof(int?)));
        Assert.Equal([SandboxValue.FromBool(false), SandboxValue.FromInt32(0)], absent.Fields);

        var falseBool = Assert.IsType<RecordValue>(KernelRpcMarshaller.ToSandboxValue(false, typeof(bool?)));
        Assert.Equal([SandboxValue.FromBool(true), SandboxValue.FromBool(false)], falseBool.Fields);

        var trueBool = Assert.IsType<RecordValue>(KernelRpcMarshaller.ToSandboxValue(true, typeof(bool?)));
        Assert.Equal([SandboxValue.FromBool(true), SandboxValue.FromBool(true)], trueBool.Fields);

        var absentBool = Assert.IsType<RecordValue>(KernelRpcMarshaller.ToSandboxValue(null, typeof(bool?)));
        Assert.Equal([SandboxValue.FromBool(false), SandboxValue.FromBool(false)], absentBool.Fields);
    }

    [Fact]
    public void FromSandboxValue_decodes_nullable_scalar_records()
    {
        var present = SandboxValue.FromRecord([SandboxValue.FromBool(true), SandboxValue.FromInt32(8)]);
        var absent = SandboxValue.FromRecord([SandboxValue.FromBool(false), SandboxValue.FromInt32(0)]);
        var falseBool = SandboxValue.FromRecord([SandboxValue.FromBool(true), SandboxValue.FromBool(false)]);
        var trueBool = SandboxValue.FromRecord([SandboxValue.FromBool(true), SandboxValue.FromBool(true)]);

        Assert.Equal(8, KernelRpcMarshaller.FromSandboxValue(present, typeof(int?)));
        Assert.Null(KernelRpcMarshaller.FromSandboxValue(absent, typeof(int?)));
        Assert.Equal(false, KernelRpcMarshaller.FromSandboxValue(falseBool, typeof(bool?)));
        Assert.Equal(true, KernelRpcMarshaller.FromSandboxValue(trueBool, typeof(bool?)));
    }

    [Fact]
    public void SandboxTypeOf_maps_nullable_scalar_types_to_has_value_records()
        => Assert.Equal(
            SandboxType.Record([SandboxType.Bool, SandboxType.I32]),
            KernelRpcMarshaller.SandboxTypeOf(typeof(int?)));

    [Fact]
    public void SandboxTypeOf_rejects_unsupported_nullable_value_types()
        => Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.SandboxTypeOf(typeof(DateTime?)));

    [Fact]
    public void SandboxTypeOf_rejects_nullable_scalar_types_past_composite_depth()
        => Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.SandboxTypeOf(typeof(int?[][][][][][][][])));

    [Fact]
    public void SandboxTypeOf_rejects_a_self_referential_dto_instead_of_stack_overflowing()
        => Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.SandboxTypeOf(typeof(SelfReferentialDto)));

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

    [Fact]
    public void ToSandboxValue_ignores_inherited_dto_properties()
    {
        var dto = new DerivedDto(10, true);

        var sandbox = KernelRpcMarshaller.ToSandboxValue(dto, typeof(DerivedDto));

        var record = Assert.IsType<RecordValue>(sandbox);
        Assert.Equal(
            [SandboxValue.FromInt32(10), SandboxValue.FromBool(true)],
            record.Fields);
    }

    [Fact]
    public void FromSandboxValue_uses_declared_dto_properties_only()
    {
        var sandbox = SandboxValue.FromRecord(
            [SandboxValue.FromInt32(11), SandboxValue.FromBool(false)]);

        var dto = Assert.IsType<DerivedDto>(
            KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(DerivedDto)));

        Assert.Equal(11, dto.MonsterId);
        Assert.False(dto.Success);
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

    private abstract class BaseDto
    {
        public int BaseId => 99;
    }

    private sealed class DerivedDto(int monsterId, bool success) : BaseDto
    {
        public int MonsterId { get; } = monsterId;

        public bool Success { get; } = success;
    }

    // A DTO whose field type is itself — SandboxTypeOf must fail the depth guard rather than recurse forever.
    private sealed class SelfReferentialDto
    {
        public int Value { get; init; }

        public SelfReferentialDto? Next { get; init; }
    }
}

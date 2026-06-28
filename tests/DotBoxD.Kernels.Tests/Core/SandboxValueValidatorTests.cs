using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Tests.Core;

public sealed class SandboxValueValidatorTests
{
    [Fact]
    public void RequireType_accepts_nested_structural_values()
    {
        var recordType = SandboxType.Record([
            SandboxType.Map(SandboxType.String, SandboxType.I32),
            SandboxType.List(SandboxType.Record([SandboxType.I32, SandboxType.String]))
        ]);
        var value = SandboxValue.FromRecord([
            SandboxValue.FromMap(
                new Dictionary<SandboxValue, SandboxValue>
                {
                    [SandboxValue.FromString("one")] = SandboxValue.FromInt32(1)
                },
                SandboxType.String,
                SandboxType.I32),
            SandboxValue.FromList(
                [
                    SandboxValue.FromRecord([
                        SandboxValue.FromInt32(7),
                        SandboxValue.FromString("ok")
                    ])
                ],
                SandboxType.Record([SandboxType.I32, SandboxType.String]))
        ]);

        SandboxValueValidator.RequireType(value, recordType, "bad input");
    }

    [Fact]
    public void RequireType_rejects_record_field_type_mismatch()
    {
        var value = SandboxValue.FromRecord([SandboxValue.FromString("wrong")]);
        var ex = Assert.Throws<SandboxRuntimeException>(() =>
            SandboxValueValidator.RequireType(
                value,
                SandboxType.Record([SandboxType.I32]),
                "bad input"));

        Assert.Equal(SandboxErrorCode.InvalidInput, ex.Error.Code);
    }

    [Fact]
    public void RequireType_rejects_empty_list_item_type_mismatch()
    {
        var value = SandboxValue.FromList([], SandboxType.I32);

        var ex = Assert.Throws<SandboxRuntimeException>(() =>
            SandboxValueValidator.RequireType(
                value,
                SandboxType.List(SandboxType.String),
                "bad input"));

        Assert.Equal(SandboxErrorCode.InvalidInput, ex.Error.Code);
    }

    [Fact]
    public void RequireType_rejects_empty_map_key_value_type_mismatch()
    {
        var value = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>(),
            SandboxType.String,
            SandboxType.I32);

        var ex = Assert.Throws<SandboxRuntimeException>(() =>
            SandboxValueValidator.RequireType(
                value,
                SandboxType.Map(SandboxType.I32, SandboxType.String),
                "bad input"));

        Assert.Equal(SandboxErrorCode.InvalidInput, ex.Error.Code);
    }

    [Fact]
    public void Validated_shape_meter_rejects_nested_record_field_type_mismatch()
    {
        var value = SandboxValue.FromRecord([
            SandboxValue.FromList([SandboxValue.FromString("wrong")], SandboxType.String)
        ]);
        var ex = Assert.Throws<SandboxRuntimeException>(() =>
            SandboxValidatedValueShapeMeter.MeasureBindingReturn(
                value,
                SandboxType.Record([SandboxType.List(SandboxType.I32)]),
                "test.binding"));

        Assert.Equal(SandboxErrorCode.BindingFailure, ex.Error.Code);
    }

    [Fact]
    public void Validated_shape_meter_counts_empty_list_depth()
    {
        var value = SandboxValue.FromList([], SandboxType.I32);

        var ex = Assert.Throws<SandboxRuntimeException>(() =>
            SandboxValidatedValueShapeMeter.Measure(
                value,
                SandboxType.List(SandboxType.I32),
                SandboxErrorCode.InvalidInput,
                "bad input",
                new ResourceLimits(MaxCollectionDepth: 0)));

        Assert.Equal(SandboxErrorCode.QuotaExceeded, ex.Error.Code);
    }

    [Fact]
    public void Validated_shape_meter_counts_empty_map_depth()
    {
        var value = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>(),
            SandboxType.String,
            SandboxType.I32);

        var ex = Assert.Throws<SandboxRuntimeException>(() =>
            SandboxValidatedValueShapeMeter.Measure(
                value,
                SandboxType.Map(SandboxType.String, SandboxType.I32),
                SandboxErrorCode.InvalidInput,
                "bad input",
                new ResourceLimits(MaxCollectionDepth: 0)));

        Assert.Equal(SandboxErrorCode.QuotaExceeded, ex.Error.Code);
    }
}

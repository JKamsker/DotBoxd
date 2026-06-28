using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionFrameworkStructMarshallerTests
{
    [Fact]
    public void Marshaller_round_trips_DateOnly_and_TimeOnly_as_scalar_wire_values()
    {
        var date = new DateOnly(2026, 6, 28);
        var time = new TimeOnly(13, 14, 15).Add(TimeSpan.FromTicks(987));

        var dateSandbox = Assert.IsType<I32Value>(
            KernelRpcMarshaller.ToSandboxValue(date, typeof(DateOnly)));
        var timeSandbox = Assert.IsType<I64Value>(
            KernelRpcMarshaller.ToSandboxValue(time, typeof(TimeOnly)));

        Assert.Equal(date.DayNumber, dateSandbox.Value);
        Assert.Equal(time.Ticks, timeSandbox.Value);
        Assert.Equal(date, KernelRpcMarshaller.FromSandboxValue(dateSandbox, typeof(DateOnly)));
        Assert.Equal(time, KernelRpcMarshaller.FromSandboxValue(timeSandbox, typeof(TimeOnly)));
        Assert.Equal(date, KernelRpcMarshaller.FromKernelRpcValue(KernelRpcValue.Int32(date.DayNumber), typeof(DateOnly)));
        Assert.Equal(time, KernelRpcMarshaller.FromKernelRpcValue(KernelRpcValue.Int64(time.Ticks), typeof(TimeOnly)));
        Assert.Equal(SandboxType.I32, KernelRpcMarshaller.SandboxTypeOf(typeof(DateOnly)));
        Assert.Equal(SandboxType.I64, KernelRpcMarshaller.SandboxTypeOf(typeof(TimeOnly)));
    }

    [Fact]
    public void Marshaller_round_trips_Index_and_Range_as_record_wire_values()
    {
        var index = Index.FromEnd(3);
        var range = new Range(Index.FromStart(2), Index.FromEnd(5));

        var indexSandbox = Assert.IsType<RecordValue>(
            KernelRpcMarshaller.ToSandboxValue(index, typeof(Index)));
        var rangeSandbox = Assert.IsType<RecordValue>(
            KernelRpcMarshaller.ToSandboxValue(range, typeof(Range)));

        AssertIndexWire(indexSandbox, index);
        AssertRangeWire(rangeSandbox, range);
        Assert.Equal(index, KernelRpcMarshaller.FromSandboxValue(indexSandbox, typeof(Index)));
        Assert.Equal(range, KernelRpcMarshaller.FromSandboxValue(rangeSandbox, typeof(Range)));
        Assert.Equal(index, KernelRpcMarshaller.FromKernelRpcValue(IndexWireValue(index), typeof(Index)));
        Assert.Equal(range, KernelRpcMarshaller.FromKernelRpcValue(RangeWireValue(range), typeof(Range)));
        Assert.Equal(SandboxType.Record([SandboxType.I32, SandboxType.Bool]), KernelRpcMarshaller.SandboxTypeOf(typeof(Index)));
        Assert.Equal(
            SandboxType.Record(
            [
                SandboxType.Record([SandboxType.I32, SandboxType.Bool]),
                SandboxType.Record([SandboxType.I32, SandboxType.Bool])
            ]),
            KernelRpcMarshaller.SandboxTypeOf(typeof(Range)));
    }

    [Fact]
    public void Marshaller_round_trips_nullable_DateOnly_and_TimeOnly()
    {
        DateOnly? date = new DateOnly(2026, 6, 28);
        TimeOnly? time = new TimeOnly(13, 14, 15);

        var dateSandbox = Assert.IsType<RecordValue>(
            KernelRpcMarshaller.ToSandboxValue(date, typeof(DateOnly?)));
        var timeSandbox = Assert.IsType<RecordValue>(
            KernelRpcMarshaller.ToSandboxValue(time, typeof(TimeOnly?)));

        Assert.Equal([SandboxValue.FromBool(true), SandboxValue.FromInt32(date.Value.DayNumber)], dateSandbox.Fields);
        Assert.Equal([SandboxValue.FromBool(true), SandboxValue.FromInt64(time.Value.Ticks)], timeSandbox.Fields);
        Assert.Equal(date, KernelRpcMarshaller.FromSandboxValue(dateSandbox, typeof(DateOnly?)));
        Assert.Equal(time, KernelRpcMarshaller.FromSandboxValue(timeSandbox, typeof(TimeOnly?)));
        Assert.Equal(
            SandboxType.Record([SandboxType.Bool, SandboxType.I32]),
            KernelRpcMarshaller.SandboxTypeOf(typeof(DateOnly?)));
        Assert.Equal(
            SandboxType.Record([SandboxType.Bool, SandboxType.I64]),
            KernelRpcMarshaller.SandboxTypeOf(typeof(TimeOnly?)));
    }

    private static KernelRpcValue IndexWireValue(Index value)
        => KernelRpcValue.Record(
        [
            KernelRpcValue.Int32(value.Value),
            KernelRpcValue.Bool(value.IsFromEnd)
        ]);

    private static KernelRpcValue RangeWireValue(Range value)
        => KernelRpcValue.Record([IndexWireValue(value.Start), IndexWireValue(value.End)]);

    private static void AssertIndexWire(RecordValue value, Index expected)
    {
        Assert.Equal(expected.Value, Assert.IsType<I32Value>(value.Fields[0]).Value);
        Assert.Equal(expected.IsFromEnd, Assert.IsType<BoolValue>(value.Fields[1]).Value);
    }

    private static void AssertRangeWire(RecordValue value, Range expected)
    {
        AssertIndexWire(Assert.IsType<RecordValue>(value.Fields[0]), expected.Start);
        AssertIndexWire(Assert.IsType<RecordValue>(value.Fields[1]), expected.End);
    }
}

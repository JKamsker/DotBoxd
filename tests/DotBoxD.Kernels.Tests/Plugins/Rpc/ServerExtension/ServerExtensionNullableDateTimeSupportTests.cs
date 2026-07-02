using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionNullableDateTimeSupportTests
{
    private const string NullableDateTimeEchoSource = """
        using System;
        using System.Threading.Tasks;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed record NullableClockPayload(DateTime? Date, DateTimeOffset? Offset);

        public interface INullableClockService
        {
            ValueTask<NullableClockPayload> EchoAsync(DateTime? date, DateTimeOffset? offset);
        }

        [ServerExtension("nullable-clock", typeof(INullableClockService))]
        public sealed partial class NullableClockKernel
        {
            public NullableClockPayload Echo(DateTime? date, DateTimeOffset? offset, HookContext ctx)
                => new(date, offset);
        }

        public static class Probe
        {
            public static ValueTask<NullableClockPayload> Echo(
                NullableClockKernelServerExtensionClient client,
                DateTime? date,
                DateTimeOffset? offset)
                => client.EchoAsync(date, offset);
        }
        """;

    [Fact]
    public void Marshaller_round_trips_nullable_DateTime_and_DateTimeOffset_as_has_value_records()
    {
        DateTime? date = new DateTime(2026, 7, 2, 9, 10, 11, DateTimeKind.Unspecified).AddTicks(12);
        DateTimeOffset? offset = new DateTimeOffset(2026, 7, 2, 9, 10, 11, TimeSpan.FromMinutes(150))
            .AddTicks(13);

        var datePresent = Assert.IsType<RecordValue>(
            KernelRpcMarshaller.ToSandboxValue(date, typeof(DateTime?)));
        var dateAbsent = Assert.IsType<RecordValue>(
            KernelRpcMarshaller.ToSandboxValue(null, typeof(DateTime?)));
        var offsetPresent = Assert.IsType<RecordValue>(
            KernelRpcMarshaller.ToSandboxValue(offset, typeof(DateTimeOffset?)));
        var offsetAbsent = Assert.IsType<RecordValue>(
            KernelRpcMarshaller.ToSandboxValue(null, typeof(DateTimeOffset?)));

        AssertNullableDateTimeSandbox(datePresent, hasValue: true, date.Value.Ticks, offsetTicks: 0L);
        AssertNullableDateTimeSandbox(dateAbsent, hasValue: false, utcTicks: 0L, offsetTicks: 0L);
        AssertNullableDateTimeSandbox(offsetPresent, hasValue: true, offset.Value.UtcTicks, offset.Value.Offset.Ticks);
        AssertNullableDateTimeSandbox(offsetAbsent, hasValue: false, utcTicks: 0L, offsetTicks: 0L);
        Assert.Equal(date, KernelRpcMarshaller.FromSandboxValue(datePresent, typeof(DateTime?)));
        Assert.Null(KernelRpcMarshaller.FromSandboxValue(dateAbsent, typeof(DateTime?)));
        Assert.Equal(offset, KernelRpcMarshaller.FromSandboxValue(offsetPresent, typeof(DateTimeOffset?)));
        Assert.Null(KernelRpcMarshaller.FromSandboxValue(offsetAbsent, typeof(DateTimeOffset?)));
        Assert.Equal(date, KernelRpcMarshaller.FromKernelRpcValue(NullableDateWire(date), typeof(DateTime?)));
        Assert.Null(KernelRpcMarshaller.FromKernelRpcValue(NullableDateWire(null), typeof(DateTime?)));
        Assert.Equal(offset, KernelRpcMarshaller.FromKernelRpcValue(NullableOffsetWire(offset), typeof(DateTimeOffset?)));
        Assert.Null(KernelRpcMarshaller.FromKernelRpcValue(NullableOffsetWire(null), typeof(DateTimeOffset?)));
        Assert.Equal(NullableDateTimeSandboxType(), KernelRpcMarshaller.SandboxTypeOf(typeof(DateTime?)));
        Assert.Equal(NullableDateTimeSandboxType(), KernelRpcMarshaller.SandboxTypeOf(typeof(DateTimeOffset?)));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public void Marshaller_rejects_nullable_DateTime_values_with_non_unspecified_kind(DateTimeKind kind)
    {
        DateTime? value = new DateTime(2026, 7, 2, 9, 10, 11, kind);

        var ex = Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.ToSandboxValue(value, typeof(DateTime?)));

        Assert.Contains("DateTimeKind.Unspecified", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generated_client_round_trips_populated_nullable_DateTime_arguments_and_null_returns()
    {
        DateTime? date = new DateTime(2026, 7, 2, 9, 10, 11, DateTimeKind.Unspecified).AddTicks(12);
        DateTimeOffset? offset = new DateTimeOffset(2026, 7, 2, 9, 10, 11, TimeSpan.FromHours(-4))
            .AddTicks(13);
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(NullableDateTimeEchoSource);
        var registry = new RecordingServerExtensionWireClient(PayloadWireBytes(date: null, offset: null));
        var client = CreateClient(assembly, registry);

        var result = await InvokeEchoAsync(assembly, client, date, offset);

        Assert.Null(PayloadProperty<DateTime?>(assembly, result, "Date"));
        Assert.Null(PayloadProperty<DateTimeOffset?>(assembly, result, "Offset"));
        var arguments = KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments);
        Assert.Equal(2, arguments.Length);
        AssertNullableDateWire(arguments[0], date);
        AssertNullableOffsetWire(arguments[1], offset);
    }

    [Fact]
    public async Task Generated_client_round_trips_null_nullable_DateTime_arguments_and_populated_returns()
    {
        DateTime? date = new DateTime(2027, 1, 3, 4, 5, 6, DateTimeKind.Unspecified).AddTicks(17);
        DateTimeOffset? offset = new DateTimeOffset(2027, 1, 3, 4, 5, 6, TimeSpan.FromMinutes(345))
            .AddTicks(19);
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(NullableDateTimeEchoSource);
        var registry = new RecordingServerExtensionWireClient(PayloadWireBytes(date, offset));
        var client = CreateClient(assembly, registry);

        var result = await InvokeEchoAsync(assembly, client, date: null, offset: null);

        Assert.Equal(date, PayloadProperty<DateTime?>(assembly, result, "Date"));
        Assert.Equal(offset, PayloadProperty<DateTimeOffset?>(assembly, result, "Offset"));
        var arguments = KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments);
        Assert.Equal(2, arguments.Length);
        AssertNullableDateWire(arguments[0], expected: null);
        AssertNullableOffsetWire(arguments[1], expected: null);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public async Task Generated_client_rejects_nullable_DateTime_arguments_with_non_unspecified_kind(DateTimeKind kind)
    {
        DateTime? date = new DateTime(2026, 7, 2, 9, 10, 11, kind);
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(NullableDateTimeEchoSource);
        var registry = new RecordingServerExtensionWireClient(PayloadWireBytes(date: null, offset: null));
        var client = CreateClient(assembly, registry);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => InvokeEchoAsync(assembly, client, date, offset: null));

        Assert.Contains("DateTimeKind.Unspecified", ex.Message, StringComparison.Ordinal);
    }

    private static object CreateClient(Assembly assembly, RecordingServerExtensionWireClient registry)
    {
        var clientType = assembly.GetType("Sample.NullableClockKernelServerExtensionClient", throwOnError: true)!;
        return clientType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [registry, "nullable-clock"])!;
    }

    private static async Task<object> InvokeEchoAsync(
        Assembly assembly,
        object client,
        DateTime? date,
        DateTimeOffset? offset)
    {
        object valueTask;
        try
        {
            valueTask = assembly.GetType("Sample.Probe", throwOnError: true)!
                .GetMethod("Echo", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [client, date, offset])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static T PayloadProperty<T>(Assembly assembly, object payload, string name)
    {
        var payloadType = assembly.GetType("Sample.NullableClockPayload", throwOnError: true)!;
        return (T)payloadType.GetProperty(name)!.GetValue(payload)!;
    }

    private static byte[] PayloadWireBytes(DateTime? date, DateTimeOffset? offset)
        => KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record([NullableDateWire(date), NullableOffsetWire(offset)]));

    private static KernelRpcValue NullableDateWire(DateTime? value)
        => NullableDateTimeWire(value.HasValue, value.GetValueOrDefault().Ticks, offsetTicks: 0L);

    private static KernelRpcValue NullableOffsetWire(DateTimeOffset? value)
        => NullableDateTimeWire(value.HasValue, value.GetValueOrDefault().UtcTicks, value.GetValueOrDefault().Offset.Ticks);

    private static KernelRpcValue NullableDateTimeWire(bool hasValue, long utcTicks, long offsetTicks)
        => KernelRpcValue.Record([KernelRpcValue.Bool(hasValue), DateTimeWire(utcTicks, offsetTicks)]);

    private static KernelRpcValue DateTimeWire(long utcTicks, long offsetTicks)
        => KernelRpcValue.Record([KernelRpcValue.Int64(utcTicks), KernelRpcValue.Int64(offsetTicks)]);

    private static void AssertNullableDateWire(KernelRpcValue value, DateTime? expected)
        => AssertNullableDateTimeWire(value, expected.HasValue, expected.GetValueOrDefault().Ticks, offsetTicks: 0L);

    private static void AssertNullableOffsetWire(KernelRpcValue value, DateTimeOffset? expected)
        => AssertNullableDateTimeWire(
            value,
            expected.HasValue,
            expected.GetValueOrDefault().UtcTicks,
            expected.GetValueOrDefault().Offset.Ticks);

    private static void AssertNullableDateTimeWire(KernelRpcValue value, bool hasValue, long utcTicks, long offsetTicks)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(2, value.ItemCount);
        Assert.Equal(hasValue, value.GetItem(0).BoolValue);
        var dateTime = value.GetItem(1);
        dateTime.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(2, dateTime.ItemCount);
        Assert.Equal(utcTicks, dateTime.GetItem(0).Int64Value);
        Assert.Equal(offsetTicks, dateTime.GetItem(1).Int64Value);
    }

    private static void AssertNullableDateTimeSandbox(RecordValue value, bool hasValue, long utcTicks, long offsetTicks)
    {
        Assert.Equal(2, value.Fields.Count);
        Assert.Equal(hasValue, Assert.IsType<BoolValue>(value.Fields[0]).Value);
        var dateTime = Assert.IsType<RecordValue>(value.Fields[1]);
        Assert.Equal(utcTicks, Assert.IsType<I64Value>(dateTime.Fields[0]).Value);
        Assert.Equal(offsetTicks, Assert.IsType<I64Value>(dateTime.Fields[1]).Value);
    }

    private static SandboxType NullableDateTimeSandboxType()
        => SandboxType.Record([SandboxType.Bool, SandboxType.Record([SandboxType.I64, SandboxType.I64])]);

    private sealed class RecordingServerExtensionWireClient(byte[] response) : DotBoxD.Plugins.IServerExtensionWireClient
    {
        public byte[] LastArguments { get; private set; } = [];

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastArguments = arguments;
            return ValueTask.FromResult(response);
        }
    }
}

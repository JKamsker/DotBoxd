using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionDateTimeSupportTests
{
    private const string DateTimeOffsetEchoSource = """
        using System;
        using System.Threading.Tasks;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public interface IClockService
        {
            ValueTask<DateTimeOffset> EchoAsync(DateTimeOffset value);
        }

        [ServerExtension("clock", typeof(IClockService))]
        public sealed partial class ClockKernel
        {
            public DateTimeOffset Echo(DateTimeOffset value, HookContext ctx)
            {
                return value;
            }
        }

        public static class Probe
        {
            public static ValueTask<DateTimeOffset> Echo(ClockKernelServerExtensionClient client, DateTimeOffset value)
                => client.EchoAsync(value);
        }
        """;

    private const string DateTimeEchoSource = """
        using System;
        using System.Threading.Tasks;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public interface IClockService
        {
            ValueTask<DateTime> EchoAsync(DateTime value);
        }

        [ServerExtension("clock", typeof(IClockService))]
        public sealed partial class ClockKernel
        {
            public DateTime Echo(DateTime value, HookContext ctx)
            {
                return value;
            }
        }

        public static class Probe
        {
            public static ValueTask<DateTime> Echo(ClockKernelServerExtensionClient client, DateTime value)
                => client.EchoAsync(value);
        }
        """;

    [Fact]
    public void Marshaller_round_trips_DateTimeOffset_as_utc_ticks_and_offset_ticks()
    {
        var value = new DateTimeOffset(2026, 6, 27, 14, 15, 16, 789, TimeSpan.FromHours(5.5))
            .AddTicks(1234);

        var sandbox = Assert.IsType<RecordValue>(
            KernelRpcMarshaller.ToSandboxValue(value, typeof(DateTimeOffset)));

        Assert.Equal(SandboxType.Record([SandboxType.I64, SandboxType.I64]), sandbox.Type);
        Assert.Equal(value.UtcTicks, Assert.IsType<I64Value>(sandbox.Fields[0]).Value);
        Assert.Equal(value.Offset.Ticks, Assert.IsType<I64Value>(sandbox.Fields[1]).Value);
        Assert.Equal(value, KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(DateTimeOffset)));
        Assert.Equal(
            value,
            KernelRpcMarshaller.FromKernelRpcValue(
                KernelRpcValue.Record([KernelRpcValue.Int64(value.UtcTicks), KernelRpcValue.Int64(value.Offset.Ticks)]),
                typeof(DateTimeOffset)));
    }

    [Fact]
    public void Marshaller_round_trips_DateTime_through_the_DateTimeOffset_wire_shape()
    {
        var value = new DateTime(2026, 6, 27, 14, 15, 16, 789, DateTimeKind.Unspecified).AddTicks(1234);

        var sandbox = Assert.IsType<RecordValue>(
            KernelRpcMarshaller.ToSandboxValue(value, typeof(DateTime)));

        Assert.Equal(SandboxType.Record([SandboxType.I64, SandboxType.I64]), sandbox.Type);
        Assert.Equal(value.Ticks, Assert.IsType<I64Value>(sandbox.Fields[0]).Value);
        Assert.Equal(0L, Assert.IsType<I64Value>(sandbox.Fields[1]).Value);
        Assert.Equal(value, KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(DateTime)));
        Assert.Equal(SandboxType.Record([SandboxType.I64, SandboxType.I64]), KernelRpcMarshaller.SandboxTypeOf(typeof(DateTime)));
        Assert.Equal(SandboxType.Record([SandboxType.I64, SandboxType.I64]), KernelRpcMarshaller.SandboxTypeOf(typeof(DateTimeOffset)));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public void Marshaller_rejects_DateTime_values_with_non_unspecified_kind(DateTimeKind kind)
    {
        var value = new DateTime(2026, 6, 27, 14, 15, 16, 789, kind);

        var ex = Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.ToSandboxValue(value, typeof(DateTime)));

        Assert.Contains("DateTimeKind.Unspecified", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Marshaller_rejects_DateTime_wire_value_with_non_zero_offset()
    {
        var offsetTicks = TimeSpan.FromHours(1).Ticks;
        var sandbox = SandboxValue.FromRecord(
            [SandboxValue.FromInt64(0), SandboxValue.FromInt64(offsetTicks)]);
        var wire = KernelRpcValue.Record([KernelRpcValue.Int64(0), KernelRpcValue.Int64(offsetTicks)]);

        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(DateTime)));
        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromKernelRpcValue(wire, typeof(DateTime)));
    }

    [Fact]
    public async Task Generated_client_round_trips_DateTimeOffset_parameters_and_returns()
    {
        var input = new DateTimeOffset(2026, 6, 27, 14, 15, 16, 789, TimeSpan.FromHours(-7))
            .AddTicks(321);
        var response = new DateTimeOffset(2027, 1, 2, 3, 4, 5, 6, TimeSpan.FromMinutes(345))
            .AddTicks(987);
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(DateTimeOffsetEchoSource);
        var registry = new RecordingServerExtensionsRegistry(DateTimeWireValue(response));
        var client = CreateClient(assembly, registry);

        var result = await InvokeEchoAsync<DateTimeOffset>(assembly, client, input);

        Assert.Equal(response, result);
        Assert.Equal(response.Offset, result.Offset);
        var argument = Assert.Single(KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments));
        AssertDateTimeWire(argument, input.UtcTicks, input.Offset.Ticks);
    }

    [Fact]
    public async Task Generated_client_round_trips_DateTime_parameters_and_returns()
    {
        var input = new DateTime(2026, 6, 27, 14, 15, 16, 789, DateTimeKind.Unspecified).AddTicks(321);
        var response = new DateTime(2027, 1, 2, 3, 4, 5, 6, DateTimeKind.Unspecified).AddTicks(987);
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(DateTimeEchoSource);
        var registry = new RecordingServerExtensionsRegistry(DateTimeWireValue(response));
        var client = CreateClient(assembly, registry);

        var result = await InvokeEchoAsync<DateTime>(assembly, client, input);

        Assert.Equal(response, result);
        var argument = Assert.Single(KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments));
        AssertDateTimeWire(argument, input.Ticks, 0L);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public async Task Generated_client_rejects_DateTime_parameters_with_non_unspecified_kind(DateTimeKind kind)
    {
        var input = new DateTime(2026, 6, 27, 14, 15, 16, 789, kind);
        var response = new DateTime(2027, 1, 2, 3, 4, 5, 6, DateTimeKind.Unspecified);
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(DateTimeEchoSource);
        var registry = new RecordingServerExtensionsRegistry(DateTimeWireValue(response));
        var client = CreateClient(assembly, registry);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => InvokeEchoAsync<DateTime>(assembly, client, input));

        Assert.Contains("DateTimeKind.Unspecified", ex.Message, StringComparison.Ordinal);
    }

    private static object CreateClient(Assembly assembly, RecordingServerExtensionsRegistry registry)
    {
        var clientType = assembly.GetType("Sample.ClockKernelServerExtensionClient", throwOnError: true)!;
        return clientType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [registry, "clock"])!;
    }

    private static async Task<T> InvokeEchoAsync<T>(Assembly assembly, object client, T value)
    {
        object valueTask;
        try
        {
            valueTask = assembly.GetType("Sample.Probe", throwOnError: true)!
                .GetMethod("Echo", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [client, value])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
        return (T)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static void AssertDateTimeWire(KernelRpcValue value, long utcTicks, long offsetTicks)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(2, value.ItemCount);
        Assert.Equal(utcTicks, value.GetItem(0).Int64Value);
        Assert.Equal(offsetTicks, value.GetItem(1).Int64Value);
    }

    private static byte[] DateTimeWireValue(DateTimeOffset value)
        => KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [KernelRpcValue.Int64(value.UtcTicks), KernelRpcValue.Int64(value.Offset.Ticks)]));

    private static byte[] DateTimeWireValue(DateTime value)
        => KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [KernelRpcValue.Int64(value.Ticks), KernelRpcValue.Int64(0L)]));

    private sealed class RecordingServerExtensionsRegistry(byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public byte[] LastArguments { get; private set; } = [];

        public string PluginId<TService>()
            where TService : class
            => "clock";

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

using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionNullableFrameworkStructSupportTests
{
    private const string NullableFrameworkEchoSource = """
        using System;
        using System.Threading.Tasks;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed record NullableFrameworkPayload(DateOnly? Date, TimeOnly? Time);

        public interface INullableFrameworkService
        {
            ValueTask<NullableFrameworkPayload> EchoAsync(DateOnly? date, TimeOnly? time);
        }

        [ServerExtension("nullable-framework", typeof(INullableFrameworkService))]
        public sealed partial class NullableFrameworkKernel
        {
            public NullableFrameworkPayload Echo(DateOnly? date, TimeOnly? time, HookContext ctx)
                => new(date, time);
        }

        public static class Probe
        {
            public static ValueTask<NullableFrameworkPayload> Echo(
                NullableFrameworkKernelServerExtensionClient client,
                DateOnly? date,
                TimeOnly? time)
                => client.EchoAsync(date, time);
        }
        """;

    [Fact]
    public async Task Generated_client_round_trips_nullable_DateOnly_and_TimeOnly_arguments_and_returns()
    {
        DateOnly? inputDate = new DateOnly(2026, 6, 28);
        TimeOnly? inputTime = null;
        DateOnly? responseDate = null;
        TimeOnly? responseTime = new TimeOnly(13, 14, 15).Add(TimeSpan.FromTicks(16));
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(NullableFrameworkEchoSource);
        var registry = new RecordingServerExtensionWireClient(PayloadWireBytes(responseDate, responseTime));
        var client = CreateClient(assembly, registry);

        var result = await InvokeEchoAsync(assembly, client, inputDate, inputTime);

        var payloadType = assembly.GetType("Sample.NullableFrameworkPayload", throwOnError: true)!;
        Assert.Equal(responseDate, (DateOnly?)payloadType.GetProperty("Date")!.GetValue(result));
        Assert.Equal(responseTime, (TimeOnly?)payloadType.GetProperty("Time")!.GetValue(result));
        var arguments = KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments);
        Assert.Equal(2, arguments.Length);
        AssertNullableDateWire(arguments[0], inputDate);
        AssertNullableTimeWire(arguments[1], inputTime);
    }

    private static object CreateClient(Assembly assembly, RecordingServerExtensionWireClient registry)
    {
        var clientType = assembly.GetType("Sample.NullableFrameworkKernelServerExtensionClient", throwOnError: true)!;
        return clientType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [registry, "nullable-framework"])!;
    }

    private static async Task<object> InvokeEchoAsync(
        Assembly assembly,
        object client,
        DateOnly? date,
        TimeOnly? time)
    {
        var valueTask = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Echo", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [client, date, time])!;
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static byte[] PayloadWireBytes(DateOnly? date, TimeOnly? time)
        => KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [NullableDateWire(date), NullableTimeWire(time)]));

    private static KernelRpcValue NullableDateWire(DateOnly? value)
        => KernelRpcValue.Record(
        [
            KernelRpcValue.Bool(value.HasValue),
            KernelRpcValue.Int32(value.GetValueOrDefault().DayNumber)
        ]);

    private static KernelRpcValue NullableTimeWire(TimeOnly? value)
        => KernelRpcValue.Record(
        [
            KernelRpcValue.Bool(value.HasValue),
            KernelRpcValue.Int64(value.GetValueOrDefault().Ticks)
        ]);

    private static void AssertNullableDateWire(KernelRpcValue value, DateOnly? expected)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(2, value.ItemCount);
        Assert.Equal(expected.HasValue, value.GetItem(0).BoolValue);
        Assert.Equal(expected.GetValueOrDefault().DayNumber, value.GetItem(1).Int32Value);
    }

    private static void AssertNullableTimeWire(KernelRpcValue value, TimeOnly? expected)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(2, value.ItemCount);
        Assert.Equal(expected.HasValue, value.GetItem(0).BoolValue);
        Assert.Equal(expected.GetValueOrDefault().Ticks, value.GetItem(1).Int64Value);
    }

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

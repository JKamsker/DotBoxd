using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionFrameworkStructSupportTests
{
    private const string FrameworkStructEchoSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed record FrameworkPayload(
            DateOnly Date,
            TimeOnly Time,
            Index Index,
            Range Range,
            Dictionary<DateOnly, TimeOnly> Map);

        public interface IFrameworkStructService
        {
            ValueTask<FrameworkPayload> EchoAsync(FrameworkPayload value);
        }

        [ServerExtension("framework", typeof(IFrameworkStructService))]
        public sealed partial class FrameworkStructKernel
        {
            public FrameworkPayload Echo(FrameworkPayload value, HookContext ctx) => value;
        }

        public static class Probe
        {
            public static ValueTask<FrameworkPayload> Echo(
                FrameworkStructKernelServerExtensionClient client,
                FrameworkPayload value)
                => client.EchoAsync(value);
        }
        """;

    [Fact]
    public async Task Generated_client_round_trips_framework_structs_in_dto_and_map_shapes()
    {
        var inputMap = new Dictionary<DateOnly, TimeOnly>
        {
            [new DateOnly(2026, 6, 28)] = new TimeOnly(8, 9, 10),
            [new DateOnly(2027, 1, 2)] = new TimeOnly(11, 12, 13).Add(TimeSpan.FromTicks(14))
        };
        var responseMap = new Dictionary<DateOnly, TimeOnly>
        {
            [new DateOnly(2030, 5, 6)] = new TimeOnly(7, 8, 9).Add(TimeSpan.FromTicks(10))
        };
        var input = new FrameworkPayload(
            new DateOnly(2026, 6, 28),
            new TimeOnly(13, 14, 15).Add(TimeSpan.FromTicks(16)),
            Index.FromEnd(3),
            new Range(Index.FromStart(2), Index.FromEnd(5)),
            inputMap);
        var response = new FrameworkPayload(
            new DateOnly(2035, 7, 8),
            new TimeOnly(9, 10, 11).Add(TimeSpan.FromTicks(12)),
            Index.FromStart(4),
            new Range(Index.FromEnd(9), Index.FromStart(12)),
            responseMap);
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(FrameworkStructEchoSource);
        var registry = new RecordingServerExtensionsRegistry(PayloadWireBytes(response));
        var client = CreateClient(assembly, registry);
        var payloadType = assembly.GetType("Sample.FrameworkPayload", throwOnError: true)!;
        var reflectedInput = CreateReflectedPayload(payloadType, input);

        var result = await InvokeEchoAsync(assembly, client, reflectedInput);

        AssertReflectedPayload(payloadType, result, response);
        var argument = Assert.Single(KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments));
        AssertPayloadWire(argument, input);
    }

    [Fact]
    public async Task Generated_client_rejects_invalid_Index_wire_values()
    {
        var input = new FrameworkPayload(
            new DateOnly(2026, 6, 28),
            new TimeOnly(13, 14, 15),
            Index.FromEnd(3),
            new Range(Index.FromStart(2), Index.FromEnd(5)),
            []);
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(FrameworkStructEchoSource);
        var registry = new RecordingServerExtensionsRegistry(InvalidIndexPayloadWireBytes());
        var client = CreateClient(assembly, registry);
        var payloadType = assembly.GetType("Sample.FrameworkPayload", throwOnError: true)!;
        var reflectedInput = CreateReflectedPayload(payloadType, input);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => InvokeEchoAsync(assembly, client, reflectedInput));

        Assert.Contains("Index wire value", ex.Message, StringComparison.Ordinal);
    }

    private static object CreateClient(Assembly assembly, RecordingServerExtensionsRegistry registry)
    {
        var clientType = assembly.GetType("Sample.FrameworkStructKernelServerExtensionClient", throwOnError: true)!;
        return clientType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [registry, "framework"])!;
    }

    private static object CreateReflectedPayload(Type payloadType, FrameworkPayload value)
        => Activator.CreateInstance(
            payloadType,
            [value.Date, value.Time, value.Index, value.Range, value.Map])!;

    private static async Task<object> InvokeEchoAsync(Assembly assembly, object client, object value)
    {
        var valueTask = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Echo", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [client, value])!;
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static byte[] PayloadWireBytes(FrameworkPayload value)
        => KernelRpcBinaryCodec.EncodeValue(PayloadWireValue(value));

    private static byte[] InvalidIndexPayloadWireBytes()
        => KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
        [
            KernelRpcValue.Int32(new DateOnly(2026, 6, 28).DayNumber),
            KernelRpcValue.Int64(new TimeOnly(13, 14, 15).Ticks),
            KernelRpcValue.Record([KernelRpcValue.Int32(-1), KernelRpcValue.Bool(false)]),
            RangeWireValue(new Range(Index.FromStart(2), Index.FromEnd(5))),
            MapWireValue([])
        ]));

    private static KernelRpcValue PayloadWireValue(FrameworkPayload value)
        => KernelRpcValue.Record(
        [
            KernelRpcValue.Int32(value.Date.DayNumber),
            KernelRpcValue.Int64(value.Time.Ticks),
            IndexWireValue(value.Index),
            RangeWireValue(value.Range),
            MapWireValue(value.Map)
        ]);

    private static KernelRpcValue MapWireValue(Dictionary<DateOnly, TimeOnly> map)
    {
        var entries = new List<KernelRpcValue>(map.Count * 2);
        foreach (var pair in map)
        {
            entries.Add(KernelRpcValue.Int32(pair.Key.DayNumber));
            entries.Add(KernelRpcValue.Int64(pair.Value.Ticks));
        }

        return KernelRpcValue.Map([.. entries]);
    }

    private static KernelRpcValue IndexWireValue(Index value)
        => KernelRpcValue.Record(
        [
            KernelRpcValue.Int32(value.Value),
            KernelRpcValue.Bool(value.IsFromEnd)
        ]);

    private static KernelRpcValue RangeWireValue(Range value)
        => KernelRpcValue.Record([IndexWireValue(value.Start), IndexWireValue(value.End)]);

    private static void AssertReflectedPayload(Type type, object value, FrameworkPayload expected)
    {
        Assert.Equal(expected.Date, type.GetProperty("Date")!.GetValue(value));
        Assert.Equal(expected.Time, type.GetProperty("Time")!.GetValue(value));
        Assert.Equal(expected.Index, type.GetProperty("Index")!.GetValue(value));
        Assert.Equal(expected.Range, type.GetProperty("Range")!.GetValue(value));
        var actualMap = Assert.IsType<Dictionary<DateOnly, TimeOnly>>(type.GetProperty("Map")!.GetValue(value));
        AssertDictionaryEqual(expected.Map, actualMap);
    }

    private static void AssertPayloadWire(KernelRpcValue value, FrameworkPayload expected)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(5, value.ItemCount);
        Assert.Equal(expected.Date.DayNumber, value.GetItem(0).Int32Value);
        Assert.Equal(expected.Time.Ticks, value.GetItem(1).Int64Value);
        AssertIndexWire(value.GetItem(2), expected.Index);
        AssertRangeWire(value.GetItem(3), expected.Range);
        AssertMapWire(value.GetItem(4), expected.Map);
    }

    private static void AssertIndexWire(KernelRpcValue value, Index expected)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(2, value.ItemCount);
        Assert.Equal(expected.Value, value.GetItem(0).Int32Value);
        Assert.Equal(expected.IsFromEnd, value.GetItem(1).BoolValue);
    }

    private static void AssertRangeWire(KernelRpcValue value, Range expected)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(2, value.ItemCount);
        AssertIndexWire(value.GetItem(0), expected.Start);
        AssertIndexWire(value.GetItem(1), expected.End);
    }

    private static void AssertMapWire(KernelRpcValue value, Dictionary<DateOnly, TimeOnly> expected)
    {
        value.RequireKind(KernelRpcValueKind.Map);
        Assert.Equal(expected.Count * 2, value.ItemCount);
        var actual = new Dictionary<DateOnly, TimeOnly>(expected.Count);
        for (var i = 0; i < value.ItemCount; i += 2)
        {
            actual[DateOnly.FromDayNumber(value.GetItem(i).Int32Value)] = new TimeOnly(value.GetItem(i + 1).Int64Value);
        }

        AssertDictionaryEqual(expected, actual);
    }

    private static void AssertDictionaryEqual(
        Dictionary<DateOnly, TimeOnly> expected,
        Dictionary<DateOnly, TimeOnly> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var pair in expected)
        {
            Assert.True(actual.TryGetValue(pair.Key, out var actualValue), $"Missing key {pair.Key}.");
            Assert.Equal(pair.Value, actualValue);
        }
    }

    private sealed record FrameworkPayload(
        DateOnly Date,
        TimeOnly Time,
        Index Index,
        Range Range,
        Dictionary<DateOnly, TimeOnly> Map);

    private sealed class RecordingServerExtensionsRegistry(byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public byte[] LastArguments { get; private set; } = [];

        public string PluginId<TService>()
            where TService : class
            => "framework";

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

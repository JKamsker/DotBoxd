using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionTimeSpanSupportTests
{
    private const string TimeSpanScalarEchoSource = """
        using System;
        using System.Threading.Tasks;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public interface ITimerService
        {
            ValueTask<TimeSpan> EchoAsync(TimeSpan value);
        }

        [ServerExtension("timer", typeof(ITimerService))]
        public sealed partial class TimerKernel
        {
            public TimeSpan Echo(TimeSpan value, HookContext ctx) => value;
        }

        public static class Probe
        {
            public static ValueTask<TimeSpan> Echo(TimerKernelServerExtensionClient client, TimeSpan value)
                => client.EchoAsync(value);
        }
        """;

    private const string TimeSpanMapEchoSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public interface ITimerService
        {
            ValueTask<Dictionary<TimeSpan, TimeSpan>> EchoMapAsync(Dictionary<TimeSpan, TimeSpan> value);
        }

        [ServerExtension("timer", typeof(ITimerService))]
        public sealed partial class TimerKernel
        {
            public Dictionary<TimeSpan, TimeSpan> EchoMap(Dictionary<TimeSpan, TimeSpan> value, HookContext ctx) => value;
        }

        public static class Probe
        {
            public static ValueTask<Dictionary<TimeSpan, TimeSpan>> EchoMap(
                TimerKernelServerExtensionClient client,
                Dictionary<TimeSpan, TimeSpan> value)
                => client.EchoMapAsync(value);
        }
        """;

    private const string TimeSpanDtoEchoSource = """
        using System;
        using System.Threading.Tasks;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed record TimerWindow(TimeSpan StartsAfter, TimeSpan Duration);

        public interface ITimerService
        {
            ValueTask<TimerWindow> EchoWindowAsync(TimerWindow value);
        }

        [ServerExtension("timer", typeof(ITimerService))]
        public sealed partial class TimerKernel
        {
            public TimerWindow EchoWindow(TimerWindow value, HookContext ctx) => value;
        }

        public static class Probe
        {
            public static ValueTask<TimerWindow> EchoWindow(TimerKernelServerExtensionClient client, TimerWindow value)
                => client.EchoWindowAsync(value);
        }
        """;

    [Fact]
    public void Marshaller_round_trips_TimeSpan_as_ticks()
    {
        var value = TimeSpan.FromTicks(-1_234_567_890_123L);

        var sandbox = Assert.IsType<I64Value>(
            KernelRpcMarshaller.ToSandboxValue(value, typeof(TimeSpan)));

        Assert.Equal(SandboxType.I64, sandbox.Type);
        Assert.Equal(value.Ticks, sandbox.Value);
        Assert.Equal(value, KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(TimeSpan)));
        Assert.Equal(value, KernelRpcMarshaller.FromKernelRpcValue(KernelRpcValue.Int64(value.Ticks), typeof(TimeSpan)));
        Assert.Equal(SandboxType.I64, KernelRpcMarshaller.SandboxTypeOf(typeof(TimeSpan)));
    }

    [Fact]
    public void Marshaller_round_trips_nullable_TimeSpan_as_has_value_ticks_record()
    {
        var value = TimeSpan.FromTicks(9_876_543_210L);

        var present = Assert.IsType<RecordValue>(
            KernelRpcMarshaller.ToSandboxValue(value, typeof(TimeSpan?)));
        var absent = Assert.IsType<RecordValue>(
            KernelRpcMarshaller.ToSandboxValue(null, typeof(TimeSpan?)));

        Assert.Equal([SandboxValue.FromBool(true), SandboxValue.FromInt64(value.Ticks)], present.Fields);
        Assert.Equal([SandboxValue.FromBool(false), SandboxValue.FromInt64(0L)], absent.Fields);
        Assert.Equal(value, KernelRpcMarshaller.FromSandboxValue(present, typeof(TimeSpan?)));
        Assert.Null(KernelRpcMarshaller.FromSandboxValue(absent, typeof(TimeSpan?)));
        Assert.Equal(
            SandboxType.Record([SandboxType.Bool, SandboxType.I64]),
            KernelRpcMarshaller.SandboxTypeOf(typeof(TimeSpan?)));
    }

    [Fact]
    public void Marshaller_round_trips_dto_with_TimeSpan_members_as_ticks()
    {
        var value = new TimeSpanWindow(TimeSpan.FromTicks(123), TimeSpan.FromTicks(-456));

        var sandbox = Assert.IsType<RecordValue>(
            KernelRpcMarshaller.ToSandboxValue(value, typeof(TimeSpanWindow)));
        var roundTripped = Assert.IsType<TimeSpanWindow>(
            KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(TimeSpanWindow)));

        Assert.Equal(
            [SandboxValue.FromInt64(value.StartsAfter.Ticks), SandboxValue.FromInt64(value.Duration.Ticks)],
            sandbox.Fields);
        Assert.Equal(value, roundTripped);
        Assert.Equal(
            SandboxType.Record([SandboxType.I64, SandboxType.I64]),
            KernelRpcMarshaller.SandboxTypeOf(typeof(TimeSpanWindow)));
    }

    [Fact]
    public async Task Generated_client_round_trips_TimeSpan_parameters_and_returns()
    {
        var input = TimeSpan.FromTicks(1_234_567_890L);
        var response = TimeSpan.FromTicks(-9_876_543_210L);
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(TimeSpanScalarEchoSource);
        var registry = new RecordingServerExtensionsRegistry(TimeSpanWireValue(response));
        var client = CreateClient(assembly, registry);

        var result = await InvokeProbeAsync<TimeSpan>(assembly, client, "Echo", input);

        Assert.Equal(response, result);
        var argument = Assert.Single(KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments));
        Assert.Equal(input.Ticks, argument.Int64Value);
    }

    [Fact]
    public async Task Generated_client_round_trips_TimeSpan_map_keys_and_values()
    {
        var firstKey = TimeSpan.FromSeconds(5).Add(TimeSpan.FromTicks(7));
        var firstValue = TimeSpan.FromMinutes(2).Add(TimeSpan.FromTicks(11));
        var secondKey = TimeSpan.FromTicks(-123);
        var secondValue = TimeSpan.FromTicks(456);
        var input = new Dictionary<TimeSpan, TimeSpan>
        {
            [firstKey] = firstValue,
            [secondKey] = secondValue
        };
        var response = new Dictionary<TimeSpan, TimeSpan>
        {
            [TimeSpan.FromHours(3)] = TimeSpan.FromTicks(-789)
        };
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(TimeSpanMapEchoSource);
        var registry = new RecordingServerExtensionsRegistry(TimeSpanMapWireValue(response));
        var client = CreateClient(assembly, registry);

        var result = await InvokeProbeAsync<Dictionary<TimeSpan, TimeSpan>>(assembly, client, "EchoMap", input);

        Assert.Equal(response, result);
        var argument = Assert.Single(KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments));
        Assert.Equal(input, ReadTimeSpanMap(argument));
    }

    [Fact]
    public async Task Generated_client_round_trips_TimeSpan_dto_members()
    {
        var inputStarts = TimeSpan.FromTicks(135);
        var inputDuration = TimeSpan.FromTicks(-246);
        var responseStarts = TimeSpan.FromTicks(357);
        var responseDuration = TimeSpan.FromTicks(-468);
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(TimeSpanDtoEchoSource);
        var windowType = assembly.GetType("Sample.TimerWindow", throwOnError: true)!;
        var input = Activator.CreateInstance(windowType, [inputStarts, inputDuration])!;
        var registry = new RecordingServerExtensionsRegistry(
            TimeSpanWindowWireValue(responseStarts, responseDuration));
        var client = CreateClient(assembly, registry);

        var result = await InvokeProbeAsync<object>(assembly, client, "EchoWindow", input);

        Assert.Equal(responseStarts, windowType.GetProperty("StartsAfter")!.GetValue(result));
        Assert.Equal(responseDuration, windowType.GetProperty("Duration")!.GetValue(result));
        var argument = Assert.Single(KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments));
        AssertTimeSpanWindowWire(argument, inputStarts, inputDuration);
    }

    private static object CreateClient(Assembly assembly, RecordingServerExtensionsRegistry registry)
    {
        var clientType = assembly.GetType("Sample.TimerKernelServerExtensionClient", throwOnError: true)!;
        return clientType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [registry, "timer"])!;
    }

    private static async Task<T> InvokeProbeAsync<T>(
        Assembly assembly,
        object client,
        string methodName,
        object value)
    {
        var valueTask = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [client, value])!;
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
        return (T)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static byte[] TimeSpanWireValue(TimeSpan value)
        => KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int64(value.Ticks));

    private static byte[] TimeSpanMapWireValue(Dictionary<TimeSpan, TimeSpan> value)
    {
        var entries = new List<KernelRpcValue>(value.Count * 2);
        foreach (var pair in value)
        {
            entries.Add(KernelRpcValue.Int64(pair.Key.Ticks));
            entries.Add(KernelRpcValue.Int64(pair.Value.Ticks));
        }

        return KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Map([.. entries]));
    }

    private static byte[] TimeSpanWindowWireValue(TimeSpan startsAfter, TimeSpan duration)
        => KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [KernelRpcValue.Int64(startsAfter.Ticks), KernelRpcValue.Int64(duration.Ticks)]));

    private static void AssertTimeSpanWindowWire(KernelRpcValue value, TimeSpan startsAfter, TimeSpan duration)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(2, value.ItemCount);
        Assert.Equal(startsAfter.Ticks, value.GetItem(0).Int64Value);
        Assert.Equal(duration.Ticks, value.GetItem(1).Int64Value);
    }

    private static Dictionary<TimeSpan, TimeSpan> ReadTimeSpanMap(KernelRpcValue map)
    {
        map.RequireKind(KernelRpcValueKind.Map);
        var result = new Dictionary<TimeSpan, TimeSpan>(map.ItemCount / 2);
        for (var i = 0; i < map.ItemCount; i += 2)
        {
            result[new TimeSpan(map.GetItem(i).Int64Value)] = new TimeSpan(map.GetItem(i + 1).Int64Value);
        }

        return result;
    }

    private sealed record TimeSpanWindow(TimeSpan StartsAfter, TimeSpan Duration);

    private sealed class RecordingServerExtensionsRegistry(byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public byte[] LastArguments { get; private set; } = [];

        public string PluginId<TService>()
            where TService : class
            => "timer";

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

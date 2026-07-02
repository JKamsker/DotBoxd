using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private const string FloatPayloadGuardSource = Prelude + """
        public sealed record FloatPayloadEvent(float Amount);

        public static class FloatPayloadGuardUsage
        {
            public static readonly System.Collections.Generic.List<float> Received = new();

            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<FloatPayloadEvent>()
                    .Select(e => e.Amount)
                    .RunLocal((amount, ctx) => Received.Add(amount));
        }
        """;

    private const string EnumPayloadGuardSource = Prelude + """
        public enum Tiny : byte
        {
            Zero = 0,
            FortyFour = 44
        }

        public sealed record EnumPayloadEvent(Tiny Value);

        public static class EnumPayloadGuardUsage
        {
            public static readonly System.Collections.Generic.List<Tiny> Received = new();

            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<EnumPayloadEvent>()
                    .Select(e => e.Value)
                    .RunLocal((value, ctx) => Received.Add(value));
        }
        """;

    private const string MapPayloadGuardSource = Prelude + """
        public sealed record MapPayloadEvent(System.Collections.Generic.Dictionary<string, int> Scores);

        public static class MapPayloadGuardUsage
        {
            public static readonly System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, int>> Received = new();

            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<MapPayloadEvent>()
                    .Select(e => e.Scores)
                    .RunLocal((scores, ctx) => Received.Add(scores));
        }
        """;

    [Fact]
    public async Task Generated_payload_reader_rejects_float_overflow_projection()
        => await AssertGeneratedPayloadReaderRejects(
            FloatPayloadGuardSource,
            "ChainSample.FloatPayloadGuardUsage",
            KernelRpcValue.Double(double.MaxValue));

    [Fact]
    public async Task Generated_payload_reader_rejects_narrow_enum_overflow_projection()
        => await AssertGeneratedPayloadReaderRejects(
            EnumPayloadGuardSource,
            "ChainSample.EnumPayloadGuardUsage",
            KernelRpcValue.Int32(300));

    [Fact]
    public async Task Generated_payload_reader_rejects_odd_map_entry_count()
        => await AssertGeneratedPayloadReaderRejects<FormatException>(
            MapPayloadGuardSource,
            "ChainSample.MapPayloadGuardUsage",
            [(byte)KernelRpcValueKind.Map, 1]);

    private static async Task AssertGeneratedPayloadReaderRejects(
        string source,
        string usageTypeName,
        KernelRpcValue payload)
        => await AssertGeneratedPayloadReaderRejects(
            source,
            usageTypeName,
            KernelRpcBinaryCodec.EncodeValue(payload));

    private static async Task AssertGeneratedPayloadReaderRejects(
        string source,
        string usageTypeName,
        byte[] payload)
        => await AssertGeneratedPayloadReaderRejects<NotSupportedException>(
            source,
            usageTypeName,
            payload);

    private static async Task AssertGeneratedPayloadReaderRejects<TException>(
        string source,
        string usageTypeName,
        byte[] payload)
        where TException : Exception
    {
        var assembly = Compile(source, enableInterceptors: true);
        string? subscriptionId = null;
        var localHandlers = new RemoteLocalHandlerRegistry();
        var registry = new RemoteHookRegistry(
            package =>
            {
                subscriptionId = package.CallbackSubscriptionId ?? package.Manifest.PluginId;
                return ValueTask.FromResult(subscriptionId);
            },
            localHandlers);

        var usage = assembly.GetType(usageTypeName, throwOnError: true)!;
        usage.GetMethod("Configure", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [registry]);

        Assert.NotNull(subscriptionId);
        await Assert.ThrowsAsync<TException>(async () =>
            await localHandlers.DispatchAsync(
                subscriptionId!,
                payload,
                new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None)));
    }
}

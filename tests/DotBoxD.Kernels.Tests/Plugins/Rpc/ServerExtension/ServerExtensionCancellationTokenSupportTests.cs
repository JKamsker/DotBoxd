using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionCancellationTokenSupportTests
{
    private const string CancellationTokenEchoSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed record TokenPayload(
            CancellationToken Token,
            List<CancellationToken> Tokens,
            Dictionary<string, CancellationToken> Map);

        public interface ICancellationTokenService
        {
            ValueTask<TokenPayload> EchoAsync(CancellationToken token, TokenPayload payload, int marker);
        }

        [ServerExtension("cancellation-token", typeof(ICancellationTokenService))]
        public sealed partial class CancellationTokenKernel
        {
            public TokenPayload Echo(CancellationToken token, TokenPayload payload, int marker, HookContext ctx)
                => payload;
        }

        public static class Probe
        {
            public static ValueTask<TokenPayload> Echo(
                CancellationTokenKernelServerExtensionClient client,
                CancellationToken token,
                TokenPayload payload,
                int marker)
                => client.EchoAsync(token, payload, marker);
        }
        """;

    [Fact]
    public async Task Generated_client_round_trips_cancellation_tokens_as_payload_values()
    {
        var topLevelToken = new CancellationToken(canceled: true);
        var input = new TokenPayload(
            new CancellationToken(canceled: false),
            [new CancellationToken(canceled: true), new CancellationToken(canceled: false)],
            TokenMap(("canceled", new CancellationToken(canceled: true)), ("active", new CancellationToken(canceled: false))));
        var response = new TokenPayload(
            new CancellationToken(canceled: false),
            [new CancellationToken(canceled: false), new CancellationToken(canceled: true)],
            TokenMap(("returned", new CancellationToken(canceled: true))));
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(CancellationTokenEchoSource);
        var registry = new RecordingServerExtensionsRegistry(PayloadWireBytes(response));
        var client = CreateClient(assembly, registry);
        var payloadType = assembly.GetType("Sample.TokenPayload", throwOnError: true)!;
        var reflectedInput = CreateReflectedPayload(payloadType, input);

        var result = await InvokeEchoAsync(assembly, client, topLevelToken, reflectedInput, marker: 42);

        AssertReflectedPayload(payloadType, result, response);
        var arguments = KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments);
        Assert.Equal(3, arguments.Length);
        Assert.True(arguments[0].BoolValue);
        AssertPayloadWire(arguments[1], input);
        Assert.Equal(42, arguments[2].Int32Value);
    }

    private static object CreateClient(Assembly assembly, RecordingServerExtensionsRegistry registry)
    {
        var clientType = assembly.GetType("Sample.CancellationTokenKernelServerExtensionClient", throwOnError: true)!;
        return clientType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [registry, "cancellation-token"])!;
    }

    private static object CreateReflectedPayload(Type payloadType, TokenPayload value)
        => Activator.CreateInstance(payloadType, [value.Token, value.Tokens, value.Map])!;

    private static async Task<object> InvokeEchoAsync(
        Assembly assembly,
        object client,
        CancellationToken token,
        object payload,
        int marker)
    {
        var valueTask = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Echo", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [client, token, payload, marker])!;
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static byte[] PayloadWireBytes(TokenPayload value)
        => KernelRpcBinaryCodec.EncodeValue(PayloadWireValue(value));

    private static KernelRpcValue PayloadWireValue(TokenPayload value)
        => KernelRpcValue.Record(
        [
            KernelRpcValue.Bool(value.Token.IsCancellationRequested),
            ListWireValue(value.Tokens),
            MapWireValue(value.Map)
        ]);

    private static KernelRpcValue ListWireValue(List<CancellationToken> tokens)
        => KernelRpcValue.List([.. tokens.Select(token => KernelRpcValue.Bool(token.IsCancellationRequested))]);

    private static KernelRpcValue MapWireValue(Dictionary<string, CancellationToken> map)
    {
        var entries = new List<KernelRpcValue>(map.Count * 2);
        foreach (var pair in map)
        {
            entries.Add(KernelRpcValue.String(pair.Key));
            entries.Add(KernelRpcValue.Bool(pair.Value.IsCancellationRequested));
        }

        return KernelRpcValue.Map([.. entries]);
    }

    private static void AssertReflectedPayload(Type type, object value, TokenPayload expected)
    {
        AssertToken(expected.Token, type.GetProperty("Token")!.GetValue(value));
        var tokens = Assert.IsType<List<CancellationToken>>(type.GetProperty("Tokens")!.GetValue(value));
        AssertTokens(expected.Tokens, tokens);
        var map = Assert.IsType<Dictionary<string, CancellationToken>>(type.GetProperty("Map")!.GetValue(value));
        AssertTokenMap(expected.Map, map);
    }

    private static void AssertPayloadWire(KernelRpcValue value, TokenPayload expected)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(3, value.ItemCount);
        Assert.Equal(expected.Token.IsCancellationRequested, value.GetItem(0).BoolValue);
        AssertListWire(value.GetItem(1), expected.Tokens);
        AssertMapWire(value.GetItem(2), expected.Map);
    }

    private static void AssertListWire(KernelRpcValue value, List<CancellationToken> expected)
    {
        value.RequireKind(KernelRpcValueKind.List);
        Assert.Equal(expected.Count, value.ItemCount);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].IsCancellationRequested, value.GetItem(i).BoolValue);
        }
    }

    private static void AssertMapWire(KernelRpcValue value, Dictionary<string, CancellationToken> expected)
    {
        value.RequireKind(KernelRpcValueKind.Map);
        Assert.Equal(expected.Count * 2, value.ItemCount);
        var actual = new Dictionary<string, bool>(expected.Count);
        for (var i = 0; i < value.ItemCount; i += 2)
        {
            actual[value.GetItem(i).TextValue] = value.GetItem(i + 1).BoolValue;
        }

        Assert.Equal(ToCancellationStateMap(expected), actual);
    }

    private static void AssertToken(CancellationToken expected, object? actual)
    {
        var token = Assert.IsType<CancellationToken>(actual);
        Assert.Equal(expected.IsCancellationRequested, token.IsCancellationRequested);
    }

    private static void AssertTokens(List<CancellationToken> expected, List<CancellationToken> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].IsCancellationRequested, actual[i].IsCancellationRequested);
        }
    }

    private static void AssertTokenMap(
        Dictionary<string, CancellationToken> expected,
        Dictionary<string, CancellationToken> actual)
        => Assert.Equal(ToCancellationStateMap(expected), ToCancellationStateMap(actual));

    private static Dictionary<string, CancellationToken> TokenMap(params (string Key, CancellationToken Token)[] entries)
    {
        var result = new Dictionary<string, CancellationToken>(entries.Length);
        foreach (var entry in entries)
        {
            result.Add(entry.Key, entry.Token);
        }

        return result;
    }

    private static Dictionary<string, bool> ToCancellationStateMap(Dictionary<string, CancellationToken> map)
        => map.ToDictionary(pair => pair.Key, pair => pair.Value.IsCancellationRequested);

    private sealed record TokenPayload(
        CancellationToken Token,
        List<CancellationToken> Tokens,
        Dictionary<string, CancellationToken> Map);

    private sealed class RecordingServerExtensionsRegistry(byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public byte[] LastArguments { get; private set; } = [];

        public string PluginId<TService>()
            where TService : class
            => "cancellation-token";

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

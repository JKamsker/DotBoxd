using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class RemoteRunLocalChainRuntimeTests
{
    [Fact]
    public void Generated_payload_list_reader_preserves_readonly_collection_shape()
    {
        var payload = KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.List([KernelRpcValue.Int32(1)]));

        var result = DecodeGeneratedObject(ReadOnlyListProjectionSource, payload);

        var list = Assert.IsAssignableFrom<IReadOnlyList<int>>(result);
        Assert.Equal([1], list);
        Assert.False(result is ICollection<int> { IsReadOnly: false });
    }

    [Fact]
    public void Generated_payload_map_reader_preserves_readonly_collection_shape()
    {
        var payload = KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Map(
        [
            KernelRpcValue.String("one"),
            KernelRpcValue.Int32(1)
        ]));

        var result = DecodeGeneratedObject(ReadOnlyMapProjectionSource, payload);

        var map = Assert.IsAssignableFrom<IReadOnlyDictionary<string, int>>(result);
        Assert.Equal(1, map["one"]);
        Assert.False(result is ICollection<KeyValuePair<string, int>> { IsReadOnly: false });
    }

    private const string ReadOnlyListProjectionSource = """
        using System.Collections.Generic;
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        public sealed record ListEvent(IReadOnlyList<int> Values);

        public static class ReadOnlyListUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<ListEvent>()
                    .Select(e => e.Values)
                    .RunLocal((values, ctx) => { });
        }
        """;

    private const string ReadOnlyMapProjectionSource = """
        using System.Collections.Generic;
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        public sealed record MapEvent(IReadOnlyDictionary<string, int> Values);

        public static class ReadOnlyMapUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<MapEvent>()
                    .Select(e => e.Values)
                    .RunLocal((values, ctx) => { });
        }
        """;
}

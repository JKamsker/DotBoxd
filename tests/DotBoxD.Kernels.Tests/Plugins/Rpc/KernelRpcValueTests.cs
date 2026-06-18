using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcValueTests
{
    [Fact]
    public void String_rejects_null_text()
        => Assert.Throws<ArgumentNullException>(() => KernelRpcValue.String(null!));

    [Fact]
    public void List_rejects_null_items()
        => Assert.Throws<ArgumentNullException>(() => KernelRpcValue.List(null!));

    [Fact]
    public void Record_rejects_null_fields()
        => Assert.Throws<ArgumentNullException>(() => KernelRpcValue.Record(null!));

    [Fact]
    public void List_copies_items_from_caller()
    {
        var items = new[] { KernelRpcValue.Int32(1) };
        var value = KernelRpcValue.List(items);

        items[0] = KernelRpcValue.Int32(2);

        Assert.Equal(1, value.Items[0].Int32Value);
    }

    [Fact]
    public void Items_returns_a_copy()
    {
        var value = KernelRpcValue.Record([KernelRpcValue.Int32(1)]);
        var items = value.Items;

        items[0] = KernelRpcValue.Int32(2);

        Assert.Equal(1, value.Items[0].Int32Value);
    }

    [Fact]
    public void Indexed_items_read_without_exposing_mutation()
    {
        var value = KernelRpcValue.Record([KernelRpcValue.Int32(1)]);
        var items = value.Items;

        items[0] = KernelRpcValue.Int32(2);

        Assert.Equal(1, value.ItemCount);
        Assert.Equal(1, value.GetItem(0).Int32Value);
    }

    [Fact]
    public void Decoded_items_return_a_copy()
    {
        var payload = KernelRpcBinaryCodec.EncodeValue(
            KernelRpcValue.List([KernelRpcValue.Int32(1)]));
        var value = KernelRpcBinaryCodec.DecodeValue(payload);
        var items = value.Items;

        items[0] = KernelRpcValue.Int32(2);

        Assert.Equal(1, value.Items[0].Int32Value);
    }

    [Fact]
    public void Generated_ipc_client_reads_rpc_items_without_cloning_array()
        => AssertGeneratedReaderUsesIndexedItems(
            ServerExtensionProxyTests.MonsterKillerWithGeneratedClientSource);

    [Fact]
    public void Generated_direct_extensions_read_rpc_items_without_cloning_array()
        => AssertGeneratedReaderUsesIndexedItems(
            ServerExtensionClientExtensionTests.DirectSyncExtensionSource);

    [Fact]
    public void Generated_list_writers_fill_rpc_arrays_for_counted_lists()
    {
        var source = string.Join(
            "\n",
            PluginAnalyzerGeneratedPackageFactory.GeneratedSources(
                ServerExtensionProxyTests.MonsterKillerWithGeneratedClientSource));

        Assert.Contains(
            "new global::DotBoxD.Plugins.KernelRpcValue[value.Count]",
            source,
            StringComparison.Ordinal);
        Assert.Contains("var __item = value[i];", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "new global::System.Collections.Generic.List<global::DotBoxD.Plugins.KernelRpcValue>()",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_list_writers_keep_foreach_fallback_for_enumerables()
    {
        var source = string.Join(
            "\n",
            PluginAnalyzerGeneratedPackageFactory.GeneratedSources(EnumerableServerExtensionSource));

        Assert.Contains(
            "new global::System.Collections.Generic.List<global::DotBoxD.Plugins.KernelRpcValue>()",
            source,
            StringComparison.Ordinal);
        Assert.Contains("foreach (var __item in value)", source, StringComparison.Ordinal);
    }

    private static void AssertGeneratedReaderUsesIndexedItems(string testSource)
    {
        var source = string.Join(
            "\n",
            PluginAnalyzerGeneratedPackageFactory.GeneratedSources(testSource));

        Assert.Contains("value.ItemCount", source, StringComparison.Ordinal);
        Assert.Contains("value.GetItem(i)", source, StringComparison.Ordinal);
        Assert.Contains("value.GetItem(0)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("value.Items", source, StringComparison.Ordinal);
    }

    private const string EnumerableServerExtensionSource = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public interface IEchoService
        {
            ValueTask<int> EchoAsync(IEnumerable<int> values);
        }

        [ServerExtension("echo", typeof(IEchoService))]
        public sealed partial class EchoKernel
        {
            public int Echo(IEnumerable<int> values, HookContext ctx)
            {
                return 0;
            }
        }
        """;
}

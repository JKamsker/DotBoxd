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
}

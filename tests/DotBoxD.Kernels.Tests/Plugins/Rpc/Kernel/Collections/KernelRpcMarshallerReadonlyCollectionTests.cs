using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcMarshallerReadonlyCollectionTests
{
    [Fact]
    public void FromSandboxValue_preserves_readonly_collection_shape()
    {
        var list = SandboxValue.FromList([SandboxValue.FromInt32(1)], SandboxType.I32);
        var restoredList = KernelRpcMarshaller.FromSandboxValue(list, typeof(IReadOnlyList<int>));

        AssertReadonlyList(restoredList);

        var map = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromInt32(1)] = SandboxValue.FromString("one")
            },
            SandboxType.I32,
            SandboxType.String);
        var restoredMap = KernelRpcMarshaller.FromSandboxValue(map, typeof(IReadOnlyDictionary<int, string>));

        AssertReadonlyMap(restoredMap);
    }

    [Fact]
    public void FromKernelRpcValue_preserves_readonly_collection_shape()
    {
        var list = KernelRpcValue.List([KernelRpcValue.Int32(1)]);
        var restoredList = KernelRpcMarshaller.FromKernelRpcValue(list, typeof(IReadOnlyList<int>));

        AssertReadonlyList(restoredList);

        var map = KernelRpcValue.Map(
        [
            KernelRpcValue.Int32(1),
            KernelRpcValue.String("one")
        ]);
        var restoredMap = KernelRpcMarshaller.FromKernelRpcValue(map, typeof(IReadOnlyDictionary<int, string>));

        AssertReadonlyMap(restoredMap);
    }

    private static void AssertReadonlyList(object? value)
    {
        var list = Assert.IsAssignableFrom<IReadOnlyList<int>>(value);
        Assert.Equal([1], list);
        Assert.False(value is ICollection<int> { IsReadOnly: false });
    }

    private static void AssertReadonlyMap(object? value)
    {
        var map = Assert.IsAssignableFrom<IReadOnlyDictionary<int, string>>(value);
        Assert.Equal("one", map[1]);
        Assert.False(value is ICollection<KeyValuePair<int, string>> { IsReadOnly: false });
    }
}

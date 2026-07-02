using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

[Collection(AllocationMeasurementCollection.Name)]
public sealed class RemoteLocalIntHandlerAllocationTests
{
    private const int WarmupIterations = 1_000;
    private const int MeasuredIterations = 100_000;

    [Fact]
    public void Int_projection_dispatch_does_not_box_the_projected_value()
    {
        var registry = new RemoteLocalHandlerRegistry();
        var context = new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None);
        var payload = EncodeProjected(42);
        var checksum = 0;
        registry.Register<int>("sub-int", (value, _) =>
        {
            checksum += value;
            return ValueTask.CompletedTask;
        });

        for (var i = 0; i < WarmupIterations; i++)
        {
            Dispatch(registry, "sub-int", payload, context);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < MeasuredIterations; i++)
        {
            Dispatch(registry, "sub-int", payload, context);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var perDispatch = (double)allocated / MeasuredIterations;

        Assert.Equal(42 * (WarmupIterations + MeasuredIterations), checksum);
        GC.KeepAlive(checksum);
        Assert.True(
            perDispatch < 8D,
            $"expected the int projection path to stay below boxing cost; observed {perDispatch:N2} B/op.");
    }

    [Fact]
    public void Int_backed_enum_projection_dispatch_does_not_box_the_projected_value()
    {
        var registry = new RemoteLocalHandlerRegistry();
        var context = new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None);
        var payload = EncodeProjected(LocalReaction.Alert);
        var checksum = 0;
        registry.Register<LocalReaction>("sub-enum", (value, _) =>
        {
            checksum += (int)value;
            return ValueTask.CompletedTask;
        });

        for (var i = 0; i < WarmupIterations; i++)
        {
            Dispatch(registry, "sub-enum", payload, context);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < MeasuredIterations; i++)
        {
            Dispatch(registry, "sub-enum", payload, context);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var perDispatch = (double)allocated / MeasuredIterations;

        Assert.Equal((int)LocalReaction.Alert * (WarmupIterations + MeasuredIterations), checksum);
        GC.KeepAlive(checksum);
        Assert.True(
            perDispatch < 8D,
            $"expected the enum projection path to stay below boxing cost; observed {perDispatch:N2} B/op.");
    }

    private static void Dispatch(
        RemoteLocalHandlerRegistry registry,
        string subscriptionId,
        byte[] payload,
        HookContext context)
        => registry.DispatchAsync(subscriptionId, payload, context).GetAwaiter().GetResult();

    private static byte[] EncodeProjected<TProjected>(TProjected value)
    {
        var sandboxValue = KernelRpcMarshaller.ToSandboxValue(value, typeof(TProjected));
        return KernelRpcBinaryCodec.EncodeValue(sandboxValue);
    }

    private enum LocalReaction
    {
        Calm,
        Alert
    }
}

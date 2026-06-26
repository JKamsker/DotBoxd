using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

[Collection(AllocationMeasurementCollection.Name)]
public sealed class RemoteLocalIntHandlerAllocationTests
{
    private const int WarmupIterations = 1_000;
    private const int MeasuredIterations = 10_000;

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
            Dispatch(registry, payload, context);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < MeasuredIterations; i++)
        {
            Dispatch(registry, payload, context);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        GC.KeepAlive(checksum);
        Assert.Equal(0L, allocated);
    }

    private static void Dispatch(RemoteLocalHandlerRegistry registry, byte[] payload, HookContext context)
        => registry.DispatchAsync("sub-int", payload, context).GetAwaiter().GetResult();

    private static byte[] EncodeProjected<TProjected>(TProjected value)
    {
        var sandboxValue = KernelRpcMarshaller.ToSandboxValue(value, typeof(TProjected));
        return KernelRpcBinaryCodec.EncodeValue(sandboxValue);
    }
}

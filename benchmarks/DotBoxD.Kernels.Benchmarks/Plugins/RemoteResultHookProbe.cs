namespace DotBoxD.Kernels.Benchmarks.Plugins;

using System.Diagnostics;
using DotBoxD.Abstractions;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Rpc;

internal static class RemoteResultHookProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 300_000;
    private const string SubscriptionId = "remote-result-hook-bench";

    public static void Run()
    {
        var scenario = new Scenario();
        for (var i = 0; i < Warmup; i++)
        {
            scenario.Dispatch();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            scenario.Dispatch();
        }

        watch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(scenario.Checksum);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Console.WriteLine(
            $"Remote result hook dispatch: {watch.Elapsed.TotalMilliseconds:N1} ms, " +
            $"{(double)allocated / Iterations:N1} B/op, checksum={scenario.Checksum:N0}");
    }

    private static byte[] EncodeProjected<T>(T value)
    {
        var sandboxValue = KernelRpcMarshaller.ToSandboxValue(value, typeof(T));
        return KernelRpcBinaryCodec.EncodeValue(sandboxValue);
    }

    private sealed class Scenario
    {
        private readonly RemoteLocalHandlerRegistry _registry = new();
        private readonly HookContext _context = new(new InMemoryPluginMessageSink(), CancellationToken.None);
        private readonly byte[] _contextPayload;

        public Scenario()
        {
            _contextPayload = EncodeProjected(new DamageCtx(21));
            _registry.RegisterResult<DamageCtx, DamageResult>(
                SubscriptionId,
                (ctx, _) => new DamageResult(true, "ok", ctx.Damage * 2));
        }

        public long Checksum { get; private set; }

        public void Dispatch()
        {
            var response = _registry.DispatchResultAsync(SubscriptionId, _contextPayload, _context)
                .GetAwaiter()
                .GetResult();
            Checksum += response.Length;
        }
    }

    private sealed record DamageCtx(int Damage);

    private readonly record struct DamageResult(bool Success, string? Reason, int Damage) : IHookResult;
}

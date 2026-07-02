namespace DotBoxD.Kernels.Benchmarks.Plugins;

using System.Diagnostics;
using DotBoxD.Plugins;

internal static class HookDispatchProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        using var single = Scenario.SinglePipeline();
        using var eight = Scenario.EightPipelines();
        using var miss = Scenario.EventMiss();

        _ = Measure(single, Warmup);
        _ = Measure(eight, Warmup);
        _ = Measure(miss, Warmup);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Write("Single pipeline", Measure(single, Iterations));
        Write("Eight event pipelines", Measure(eight, Iterations));
        Write("Event miss", Measure(miss, Iterations));
    }

    private static Measurement Measure(Scenario scenario, int iterations)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            scenario.Publish();
        }

        watch.Stop();
        return new Measurement(watch.Elapsed, GC.GetAllocatedBytesForCurrentThread() - before, iterations);
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name}: {measurement.Elapsed.TotalMilliseconds:N1} ms, " +
            $"{measurement.Elapsed.TotalNanoseconds / measurement.Iterations:N1} ns/op, " +
            $"{measurement.Allocated:N0} B, " +
            $"{(double)measurement.Allocated / measurement.Iterations:N1} B/op");

    private sealed class Scenario : IDisposable
    {
        private readonly PluginServer _server;
        private readonly Action _publish;

        private Scenario(PluginServer server, Action publish)
        {
            _server = server;
            _publish = publish;
        }

        public static Scenario SinglePipeline()
        {
            var server = PluginServer.Create();
            server.Hooks.On<BenchEvent>();
            return new Scenario(server, () =>
                server.Hooks.PublishAsync(new BenchEvent(1)).GetAwaiter().GetResult());
        }

        public static Scenario EightPipelines()
        {
            var server = PluginServer.Create();
            server.Hooks.On<BenchEvent>();
            server.Hooks.On<BenchEvent, Context1>(ctx => new Context1(ctx));
            server.Hooks.On<BenchEvent, Context2>(ctx => new Context2(ctx));
            server.Hooks.On<BenchEvent, Context3>(ctx => new Context3(ctx));
            server.Hooks.On<BenchEvent, Context4>(ctx => new Context4(ctx));
            server.Hooks.On<BenchEvent, Context5>(ctx => new Context5(ctx));
            server.Hooks.On<BenchEvent, Context6>(ctx => new Context6(ctx));
            server.Hooks.On<BenchEvent, Context7>(ctx => new Context7(ctx));
            return new Scenario(server, () =>
                server.Hooks.PublishAsync(new BenchEvent(1)).GetAwaiter().GetResult());
        }

        public static Scenario EventMiss()
        {
            var server = PluginServer.Create();
            server.Hooks.On<OtherEvent1>();
            server.Hooks.On<OtherEvent2>();
            server.Hooks.On<OtherEvent3>();
            server.Hooks.On<OtherEvent4>();
            server.Hooks.On<OtherEvent5>();
            server.Hooks.On<OtherEvent6>();
            server.Hooks.On<OtherEvent7>();
            server.Hooks.On<OtherEvent8>();
            return new Scenario(server, () =>
                server.Hooks.PublishAsync(new BenchEvent(1)).GetAwaiter().GetResult());
        }

        public void Publish() => _publish();

        public void Dispose() => _server.Dispose();
    }

    private readonly record struct Measurement(TimeSpan Elapsed, long Allocated, int Iterations);

    private readonly record struct BenchEvent(int Value);

    private readonly record struct OtherEvent1(int Value);

    private readonly record struct OtherEvent2(int Value);

    private readonly record struct OtherEvent3(int Value);

    private readonly record struct OtherEvent4(int Value);

    private readonly record struct OtherEvent5(int Value);

    private readonly record struct OtherEvent6(int Value);

    private readonly record struct OtherEvent7(int Value);

    private readonly record struct OtherEvent8(int Value);

    private readonly record struct Context1(HookContext Raw);

    private readonly record struct Context2(HookContext Raw);

    private readonly record struct Context3(HookContext Raw);

    private readonly record struct Context4(HookContext Raw);

    private readonly record struct Context5(HookContext Raw);

    private readonly record struct Context6(HookContext Raw);

    private readonly record struct Context7(HookContext Raw);
}

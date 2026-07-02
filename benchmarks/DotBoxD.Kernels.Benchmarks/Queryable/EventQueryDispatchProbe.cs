using System.Diagnostics;
using DotBoxD.Queryable.Authoring;

namespace DotBoxD.Kernels.Benchmarks.Queryable;

internal static class EventQueryDispatchProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        var broad = Scenario.Broad();
        var indexedHit = Scenario.IndexedHit();
        var indexedMiss = Scenario.IndexedMiss();

        _ = Measure(broad, Warmup);
        _ = Measure(indexedHit, Warmup);
        _ = Measure(indexedMiss, Warmup);

        Console.WriteLine("Event query dispatch probe");
        Write("Broad single subscriber", Measure(broad, Iterations));
        Write("Indexed hit", Measure(indexedHit, Iterations));
        Write("Indexed miss", Measure(indexedMiss, Iterations));
    }

    private static Measurement Measure(Scenario scenario, int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            scenario.Publish();
        }

        watch.Stop();
        return new Measurement(watch.Elapsed, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, iterations);
    }

    private static void Write(string name, Measurement measurement)
    {
        Console.WriteLine(
            $"{name,-24} {measurement.Elapsed.TotalMilliseconds,8:N1} ms " +
            $"{measurement.Elapsed.TotalNanoseconds / measurement.Iterations,8:N1} ns/op " +
            $"{measurement.Allocated,12:N0} B " +
            $"{(double)measurement.Allocated / measurement.Iterations,8:N1} B/op");
    }

    private sealed class Scenario
    {
        private readonly EventQueryHost _host = new();
        private readonly HookContext _context = new(new InMemoryPluginMessageSink(), CancellationToken.None);
        private readonly AttackEvent _event;
        private int _dispatches;

        private Scenario(AttackEvent e)
        {
            _event = e;
        }

        public static Scenario Broad()
        {
            var scenario = new Scenario(new AttackEvent("player-1", "target", 8));
            scenario._host.Query<AttackEvent>()
                .SubscribeAsync((_, _) =>
                {
                    scenario._dispatches++;
                    return ValueTask.CompletedTask;
                })
                .GetAwaiter()
                .GetResult();
            return scenario;
        }

        public static Scenario IndexedHit()
        {
            var scenario = new Scenario(new AttackEvent("player-1", "target", 8));
            scenario._host.Query<AttackEvent>()
                .Where(e => e.AttackerId == "player-1")
                .SubscribeAsync((_, _) =>
                {
                    scenario._dispatches++;
                    return ValueTask.CompletedTask;
                })
                .GetAwaiter()
                .GetResult();
            return scenario;
        }

        public static Scenario IndexedMiss()
        {
            var scenario = new Scenario(new AttackEvent("player-2", "target", 8));
            scenario._host.Query<AttackEvent>()
                .Where(e => e.AttackerId == "player-1")
                .SubscribeAsync((_, _) =>
                {
                    scenario._dispatches++;
                    return ValueTask.CompletedTask;
                })
                .GetAwaiter()
                .GetResult();
            return scenario;
        }

        public void Publish()
        {
            _host.PublishAsync(_event, _context).GetAwaiter().GetResult();
        }
    }

    private readonly record struct Measurement(TimeSpan Elapsed, long Allocated, int Iterations);

    private readonly record struct AttackEvent(string AttackerId, string TargetId, int Damage);
}

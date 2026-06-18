using System.Diagnostics;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Benchmarks.Support;
using DotBoxD.Services.Generated;
using DotBoxD.Services.Peer;
using Shared;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class RpcPeerProxyCacheProbe
{
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        var (leftConnection, rightConnection) = InMemoryPipe.CreateConnectionPair();
        var peer = RpcPeer.Over(leftConnection, new MessagePackRpcSerializer());

        try
        {
            _ = GeneratedServiceRegistry.CreateProxy<IGameService>(peer);
            _ = peer.Get<IGameService>();

            var legacyGate = new object();
            var uncached = Measure(Iterations, () => GeneratedServiceRegistry.CreateProxy<IGameService>(peer));
            var legacyExtension = Measure(Iterations, () => new GameServiceProxy(peer));
            var legacy = Measure(
                Iterations,
                () =>
                {
                    lock (legacyGate)
                    {
                        return GeneratedServiceRegistry.CreateProxy<IGameService>(peer);
                    }
                });
            var cached = Measure(Iterations, () => peer.Get<IGameService>());
            var generatedExtension = Measure(Iterations, () => peer.GetGameService());

            Console.WriteLine("RpcPeer proxy lookup probe");
            Write("Uncached registry CreateProxy", uncached);
            Write("Legacy generated Get extension", legacyExtension);
            Write("Legacy locked proxy creation", legacy);
            Write("Cached RpcPeer.Get", cached);
            Write("Cached generated Get extension", generatedExtension);
        }
        finally
        {
            peer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            rightConnection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static Measurement Measure(int iterations, Func<object> action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        object? last = null;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            last = action();
        }

        sw.Stop();
        GC.KeepAlive(last);
        return new Measurement(
            iterations,
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }

    private static void Write(string name, Measurement measurement)
    {
        Console.WriteLine(
            $"{name,-32} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,12:N0} B " +
            $"{measurement.BytesPerOperation,8:N1} B/op");
    }

    private readonly record struct Measurement(
        int Iterations,
        double Milliseconds,
        long AllocatedBytes)
    {
        public double NanosecondsPerOperation => Milliseconds * 1_000_000 / Iterations;

        public double BytesPerOperation => AllocatedBytes / (double)Iterations;
    }
}

using BenchmarkDotNet.Running;
using DotBoxD.Services.Benchmarks.Probes;

if (args.Length == 1)
{
    switch (args[0])
    {
        case "--probe-peer-proxy-cache":
            RpcPeerProxyCacheProbe.Run();
            return;
        case "--probe-stream-connection-receive-tracking":
            StreamConnectionReceiveTrackingProbe.Run();
            return;
    }
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

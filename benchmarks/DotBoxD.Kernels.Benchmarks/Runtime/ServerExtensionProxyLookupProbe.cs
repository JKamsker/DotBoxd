using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class ServerExtensionProxyLookupProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        KernelPackageRegistry.Register(typeof(LookupKernel), LookupPluginPackage.Create);
        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var pluginId = server.RegisterServerExtensionAsync<ILookupService, LookupKernel>()
            .AsTask()
            .GetAwaiter()
            .GetResult();

        _ = MeasureLegacy(server, pluginId, Warmup);
        _ = MeasureCached(server, Warmup);

        var legacy = MeasureLegacy(server, pluginId, Iterations);
        var cached = MeasureCached(server, Iterations);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Console.WriteLine(
            $"legacy ServerExtension lookup {legacy.Milliseconds,8:N1} ms " +
            $"{legacy.AllocatedBytes,14:N0} B {legacy.Checksum,12:N0} checksum");
        Console.WriteLine(
            $"cached ServerExtension lookup {cached.Milliseconds,8:N1} ms " +
            $"{cached.AllocatedBytes,14:N0} B {cached.Checksum,12:N0} checksum");
    }

    private static Measurement MeasureLegacy(PluginServer server, string pluginId, int iterations)
        => Measure(iterations, () => ServerExtensionProxy.Create<ILookupService>(server.Kernels.Get(pluginId)));

    private static Measurement MeasureCached(PluginServer server, int iterations)
        => Measure(iterations, server.ServerExtension<ILookupService>);

    private static Measurement Measure(int iterations, Func<ILookupService> lookup)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var checksum = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            checksum = unchecked((checksum * 31) + RuntimeHelpers.GetHashCode(lookup()));
        }

        sw.Stop();
        return new Measurement(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            checksum);
    }

    private static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private sealed record Measurement(double Milliseconds, long AllocatedBytes, int Checksum);

    public interface ILookupService
    {
        int Echo(int value);
    }

    public sealed class LookupKernel;

    public static class LookupPluginPackage
    {
        private static readonly SourceSpan Span = new(1, 1);

        public static PluginPackage Create()
        {
            var function = new SandboxFunction(
                "Echo",
                IsEntrypoint: true,
                [new Parameter("value", SandboxType.I32)],
                SandboxType.I32,
                [new ReturnStatement(new VariableExpression("value", Span), Span)]);
            var module = new SandboxModule(
                "probe.lookup",
                SemVersion.One,
                SemVersion.One,
                [],
                [function],
                new Dictionary<string, string> { ["pluginId"] = "probe.lookup", ["kernel"] = nameof(LookupKernel) });
            var manifest = new PluginManifest(
                "probe.lookup",
                nameof(ILookupService),
                ExecutionMode.Auto,
                ["Cpu"],
                [],
                [])
            {
                RpcEntrypoint = "Echo"
            };

            return PluginPackage.Create(manifest, module, new KernelEntrypoints("Echo", "Echo"));
        }
    }
}

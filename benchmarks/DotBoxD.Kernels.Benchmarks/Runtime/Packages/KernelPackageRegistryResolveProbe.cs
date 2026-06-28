namespace DotBoxD.Kernels.Benchmarks.Runtime;

using System.Diagnostics;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;

internal static class KernelPackageRegistryResolveProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        _ = Measure(Warmup);
        var resolved = Measure(Iterations);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Console.WriteLine(
            $"convention resolve {resolved.Milliseconds,8:N1} ms " +
            $"{resolved.AllocatedBytes,14:N0} B {resolved.Checksum,12:N0} checksum");
    }

    private static Measurement Measure(int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var checksum = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var package = KernelPackageRegistry.Resolve<RegistryResolveKernel>();
            checksum = unchecked((checksum * 31) + package.Manifest.PluginId.Length);
        }

        sw.Stop();
        return new Measurement(sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before, checksum);
    }

    private sealed record Measurement(double Milliseconds, long AllocatedBytes, int Checksum);
}

public sealed class RegistryResolveKernel;

public static class RegistryResolvePluginPackage
{
    private static readonly SourceSpan Span = new(1, 1);

    public static PluginPackage Create()
    {
        var function = new SandboxFunction(
            "Handle",
            IsEntrypoint: true,
            [],
            SandboxType.Unit,
            [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)]);
        var module = new SandboxModule(
            "probe.registry-resolve",
            SemVersion.One,
            SemVersion.One,
            [],
            [function],
            new Dictionary<string, string> { ["pluginId"] = "probe.registry-resolve" });
        var manifest = new PluginManifest(
            "probe.registry-resolve",
            "Probe",
            ExecutionMode.Auto,
            ["Cpu"],
            [],
            []);

        return PluginPackage.Create(manifest, module, new KernelEntrypoints("Handle", "Handle"));
    }
}

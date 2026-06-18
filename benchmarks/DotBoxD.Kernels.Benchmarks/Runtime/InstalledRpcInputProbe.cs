using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class InstalledRpcInputProbe
{
    private const int FunctionCount = 512;
    private const int Warmup = 10_000;
    private const int Iterations = 200_000;
    private static readonly SourceSpan Span = new(1, 1);

    public static void Run()
    {
        var package = CreatePackage(FunctionCount);
        var cachedFunction = FindRpcEntrypoint(package)!;
        var cachedCallerCount = cachedFunction.Parameters.Count - package.Manifest.LiveSettings.Count;
        var arguments = new[] { SandboxValue.FromInt32(42) };

        _ = MeasureLegacy(package, arguments, Warmup);
        _ = MeasureCached(package, cachedFunction, cachedCallerCount, arguments, Warmup);

        var legacy = MeasureLegacy(package, arguments, Iterations);
        var cached = MeasureCached(package, cachedFunction, cachedCallerCount, arguments, Iterations);

        Console.WriteLine($"functions = {FunctionCount:N0}");
        Console.WriteLine($"iterations = {Iterations:N0}");
        Write("legacy RPC input build", legacy);
        Write("cached RPC input build", cached);
    }

    private static Measurement MeasureLegacy(
        PluginPackage package,
        IReadOnlyList<SandboxValue> arguments,
        int iterations)
        => Measure(iterations, () => {
            var function = FindRpcEntrypoint(package)!;
            var callerCount = function.Parameters.Count - package.Manifest.LiveSettings.Count;
            return BuildInput(function, callerCount, package.Manifest.LiveSettings, arguments);
        });

    private static Measurement MeasureCached(
        PluginPackage package,
        SandboxFunction function,
        int callerCount,
        IReadOnlyList<SandboxValue> arguments,
        int iterations)
        => Measure(
            iterations,
            () => BuildInput(function, callerCount, package.Manifest.LiveSettings, arguments));

    private static Measurement Measure(int iterations, Func<SandboxValue> build)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var checksum = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            checksum = unchecked((checksum * 31) + RuntimeHelpers.GetHashCode(build()));
        }

        sw.Stop();
        return new Measurement(sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before, checksum);
    }

    private static SandboxValue BuildInput(
        SandboxFunction function,
        int callerCount,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        IReadOnlyList<SandboxValue> arguments)
    {
        if (arguments.Count != callerCount)
        {
            throw new InvalidOperationException("unexpected argument count");
        }

        return function.Parameters.Count == 1 ? arguments[0] : SandboxValue.Unit;
    }

    private static SandboxFunction? FindRpcEntrypoint(PluginPackage package)
    {
        var entrypoint = package.Manifest.RpcEntrypoint;
        foreach (var function in package.Module.Functions)
        {
            if (function.IsEntrypoint && string.Equals(function.Id, entrypoint, StringComparison.Ordinal))
            {
                return function;
            }
        }

        return null;
    }

    private static PluginPackage CreatePackage(int functionCount)
    {
        var functions = new SandboxFunction[functionCount];
        for (var i = 0; i < functions.Length - 1; i++)
        {
            functions[i] = Function("Helper" + i.ToString("D4"), isEntrypoint: false);
        }

        functions[^1] = Function("Invoke", isEntrypoint: true);
        var module = new SandboxModule(
            "probe.rpc-input",
            SemVersion.One,
            SemVersion.One,
            [],
            functions,
            new Dictionary<string, string> { ["pluginId"] = "probe.rpc-input", ["kernel"] = "ProbeKernel" });
        var manifest = new PluginManifest(
            "probe.rpc-input",
            "IProbe",
            ExecutionMode.Auto,
            ["Cpu"],
            [],
            [])
        {
            RpcEntrypoint = "Invoke"
        };

        return PluginPackage.Create(manifest, module, new KernelEntrypoints("Invoke", "Invoke"));
    }

    private static SandboxFunction Function(string id, bool isEntrypoint)
        => new(
            id,
            isEntrypoint,
            [new Parameter("value", SandboxType.I32)],
            SandboxType.I32,
            [new ReturnStatement(new VariableExpression("value", Span), Span)]);

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-23} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B checksum={measurement.Checksum:N0}");

    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes, int Checksum);
}

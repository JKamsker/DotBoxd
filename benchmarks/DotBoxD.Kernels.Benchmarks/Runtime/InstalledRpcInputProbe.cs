using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class InstalledRpcInputProbe
{
    private const int FunctionCount = 512;
    private const int Warmup = 10_000;
    private const int Iterations = 200_000;
    private const int BinderIterations = 1_000_000;
    private const int WireIterations = 1_000_000;
    private static readonly SourceSpan Span = new(1, 1);
    private static object? s_argumentsSink;

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
        var zeroFunction = BinderFunction("zero", []);
        var oneFunction = BinderFunction("one", [new Parameter("value", SandboxType.I32)]);
        var oneInput = SandboxValue.FromInt32(42);
        var oneRpcArguments = new[] { KernelRpcValue.Int32(42) };

        _ = MeasureLegacyZeroBind(zeroFunction, Warmup);
        _ = MeasureBind(zeroFunction, SandboxValue.Unit, Warmup);
        _ = MeasureBind(oneFunction, oneInput, Warmup);
        _ = MeasureLegacyZeroRpcArguments(Warmup);
        _ = MeasureRpcArguments([], zeroFunction.Parameters, Warmup);
        _ = MeasureRpcArguments(oneRpcArguments, oneFunction.Parameters, Warmup);

        var legacyZeroBind = MeasureLegacyZeroBind(zeroFunction, BinderIterations);
        var currentZeroBind = MeasureBind(zeroFunction, SandboxValue.Unit, BinderIterations);
        var oneBind = MeasureBind(oneFunction, oneInput, BinderIterations);
        var legacyZeroRpc = MeasureLegacyZeroRpcArguments(WireIterations);
        var currentZeroRpc = MeasureRpcArguments([], zeroFunction.Parameters, WireIterations);
        var oneRpc = MeasureRpcArguments(oneRpcArguments, oneFunction.Parameters, WireIterations);

        Console.WriteLine($"functions = {FunctionCount:N0}");
        Console.WriteLine($"iterations = {Iterations:N0}");
        Write("legacy RPC input build", legacy);
        Write("cached RPC input build", cached);
        Console.WriteLine($"binder iterations = {BinderIterations:N0}");
        Write("legacy zero bind", legacyZeroBind);
        Write("current zero bind", currentZeroBind);
        Write("one bind control", oneBind);
        Console.WriteLine($"wire arg iterations = {WireIterations:N0}");
        Write("legacy zero RPC args", legacyZeroRpc);
        Write("current zero RPC args", currentZeroRpc);
        Write("one RPC arg control", oneRpc);
    }

    private static Measurement MeasureLegacy(
        PluginPackage package,
        IReadOnlyList<SandboxValue> arguments,
        int iterations)
        => Measure(iterations, () =>
        {
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

    private static Measurement MeasureBind(SandboxFunction function, SandboxValue input, int iterations)
        => MeasureArguments(iterations, () => EntrypointBinder.BindArguments(function, input));

    private static Measurement MeasureLegacyZeroBind(SandboxFunction function, int iterations)
        => MeasureArguments(iterations, () =>
        {
            EntrypointBinder.ValidateInputShape(SandboxValue.Unit, function.Parameters.Count);
            return LegacyEmptyArray();
        });

    private static Measurement MeasureLegacyZeroRpcArguments(int iterations)
        => MeasureArguments(iterations, static () => LegacyEmptyArray());

    private static Measurement MeasureRpcArguments(
        IReadOnlyList<KernelRpcValue> rpcArguments,
        IReadOnlyList<Parameter> parameters,
        int iterations)
        => MeasureArguments(iterations, () => ConvertRpcArguments(rpcArguments, parameters));

    private static Measurement MeasureArguments(int iterations, Func<IReadOnlyList<SandboxValue>> bind)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long checksum = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var arguments = bind();
            s_argumentsSink = arguments;
            checksum += arguments.Count;
        }

        sw.Stop();
        return new Measurement(sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before, (int)checksum);
    }

    private static IReadOnlyList<SandboxValue> ConvertRpcArguments(
        IReadOnlyList<KernelRpcValue> rpcArguments,
        IReadOnlyList<Parameter> parameters)
    {
        var sandboxArguments = rpcArguments.Count == 0
            ? Array.Empty<SandboxValue>()
            : new SandboxValue[rpcArguments.Count];
        for (var i = 0; i < rpcArguments.Count; i++)
        {
            sandboxArguments[i] = KernelRpcValueConverter.ToSandboxValue(rpcArguments[i], parameters[i].Type);
        }

        return sandboxArguments;
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

    private static SandboxFunction BinderFunction(string id, IReadOnlyList<Parameter> parameters)
        => new(
            id,
            IsEntrypoint: true,
            parameters,
            SandboxType.Unit,
            [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)]);

    private static SandboxValue[] LegacyEmptyArray()
    {
#pragma warning disable MA0005 // Intentional legacy allocation measured by this probe.
        return new SandboxValue[0];
#pragma warning restore MA0005
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-23} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B checksum={measurement.Checksum:N0}");

    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes, int Checksum);
}

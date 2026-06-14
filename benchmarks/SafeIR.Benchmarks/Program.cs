using BenchmarkDotNet.Running;
using System.Globalization;
using SafeIR.Benchmarks.Ipc;

if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase)) {
    await IpcAllocationSmoke.RunAsync();
    return;
}

if (args.Contains("--probe-compiled", StringComparer.OrdinalIgnoreCase)) {
    await SafeIR.Benchmarks.Interpreter.CompiledSpeedProbe.RunAsync();
    return;
}

if (args.Contains("--probe-bindings", StringComparer.OrdinalIgnoreCase)) {
    await SafeIR.Benchmarks.Interpreter.BindingCrossingProbe.RunAsync();
    return;
}

if (args.Contains("--probe-matrix", StringComparer.OrdinalIgnoreCase)) {
    await SafeIR.Benchmarks.Interpreter.PerformanceMatrixProbe.RunAsync();
    return;
}

var profileIndex = Array.FindIndex(args, arg => arg.Equals("--profile-ipc", StringComparison.OrdinalIgnoreCase));
if (profileIndex >= 0) {
    var transport = args.ElementAtOrDefault(profileIndex + 1) ?? IpcAllocationProfile.NamedPipeTransport;
    var iterationsText = args.ElementAtOrDefault(profileIndex + 2) ?? "10000";
    var iterations = int.Parse(iterationsText, CultureInfo.InvariantCulture);
    var disableTimeout = args.Contains("--no-timeout", StringComparer.OrdinalIgnoreCase);
    var lowAllocationProfile = args.Contains("--low-alloc", StringComparer.OrdinalIgnoreCase);
    await IpcAllocationProfile.RunAsync(transport, iterations, disableTimeout, lowAllocationProfile);
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

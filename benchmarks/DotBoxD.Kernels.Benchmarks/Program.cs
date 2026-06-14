using BenchmarkDotNet.Running;
using System.Globalization;
using DotBoxD.Kernels.Benchmarks.Ipc;

if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase)) {
    await IpcAllocationSmoke.RunAsync();
    return;
}

if (args.Contains("--probe-compiled", StringComparer.OrdinalIgnoreCase)) {
    await DotBoxD.Kernels.Benchmarks.Interpreter.CompiledSpeedProbe.RunAsync();
    return;
}

if (args.Contains("--probe-bindings", StringComparer.OrdinalIgnoreCase)) {
    await DotBoxD.Kernels.Benchmarks.Interpreter.BindingCrossingProbe.RunAsync();
    return;
}

if (args.Contains("--probe-matrix", StringComparer.OrdinalIgnoreCase)) {
    await DotBoxD.Kernels.Benchmarks.Interpreter.PerformanceMatrixProbe.RunAsync();
    return;
}

if (args.Contains("--probe-rogue", StringComparer.OrdinalIgnoreCase)) {
    await DotBoxD.Kernels.Benchmarks.Interpreter.RogueScalingProbe.RunAsync();
    return;
}

if (args.Contains("--probe-examples", StringComparer.OrdinalIgnoreCase)) {
    await DotBoxD.Kernels.Benchmarks.Examples.ExampleWorkflowProbe.RunAsync();
    return;
}

if (args.Contains("--probe-prepared-values", StringComparer.OrdinalIgnoreCase)) {
    await DotBoxD.Kernels.Benchmarks.Examples.PreparedValueProbe.RunAsync();
    return;
}

if (args.Contains("--probe-runtime-types", StringComparer.OrdinalIgnoreCase)) {
    DotBoxD.Kernels.Benchmarks.Runtime.RuntimeTypeProbe.Run();
    return;
}

if (args.Contains("--probe-resource-meter", StringComparer.OrdinalIgnoreCase)) {
    DotBoxD.Kernels.Benchmarks.Runtime.ResourceMeterProbe.Run();
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

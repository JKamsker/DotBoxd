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

if (args.Contains("--probe-http-metadata", StringComparer.OrdinalIgnoreCase)) {
    DotBoxD.Kernels.Benchmarks.Http.HttpMetadataAccountingProbe.Run();
    return;
}

if (args.Contains("--probe-value-shape-cache", StringComparer.OrdinalIgnoreCase)) {
    DotBoxD.Kernels.Benchmarks.Runtime.ValueShapeCacheProbe.Run();
    return;
}

if (args.Contains("--probe-compiled-binding-fast-path", StringComparer.OrdinalIgnoreCase)) {
    DotBoxD.Kernels.Benchmarks.Runtime.CompiledBindingFastPathProbe.Run();
    return;
}

if (args.Contains("--probe-compiled-binding-structural-validation", StringComparer.OrdinalIgnoreCase)) {
    DotBoxD.Kernels.Benchmarks.Runtime.CompiledBindingStructuralValidationProbe.Run();
    return;
}

if (args.Contains("--probe-i32-math-intrinsic", StringComparer.OrdinalIgnoreCase)) {
    DotBoxD.Kernels.Benchmarks.Runtime.I32MathIntrinsicProbe.Run();
    return;
}

if (args.Contains("--probe-f64-math-intrinsic", StringComparer.OrdinalIgnoreCase)) {
    DotBoxD.Kernels.Benchmarks.Runtime.F64MathIntrinsicProbe.Run();
    return;
}

if (args.Contains("--probe-binding-return-credit", StringComparer.OrdinalIgnoreCase)) {
    DotBoxD.Kernels.Benchmarks.Runtime.BindingReturnCreditProbe.Run();
    return;
}

if (args.Contains("--probe-binding-registry", StringComparer.OrdinalIgnoreCase)) {
    DotBoxD.Kernels.Benchmarks.Core.BindingRegistryProbe.Run();
    return;
}

if (args.Contains("--probe-host-call-accounting", StringComparer.OrdinalIgnoreCase)) {
    DotBoxD.Kernels.Benchmarks.Runtime.HostCallAccountingProbe.Run();
    return;
}

if (args.Contains("--probe-binding-dispatch-scope", StringComparer.OrdinalIgnoreCase)) {
    DotBoxD.Kernels.Benchmarks.Runtime.BindingDispatchScopeProbe.Run();
    return;
}

if (args.Contains("--probe-compiled-binding-arity", StringComparer.OrdinalIgnoreCase)) {
    DotBoxD.Kernels.Benchmarks.Runtime.CompiledBindingArityProbe.Run();
    return;
}

if (args.Contains("--probe-capability-grant-lookup", StringComparer.OrdinalIgnoreCase)) {
    DotBoxD.Kernels.Benchmarks.Runtime.CapabilityGrantLookupProbe.Run();
    return;
}

if (args.Contains("--probe-server-extension-proxy-lookup", StringComparer.OrdinalIgnoreCase)) {
    DotBoxD.Kernels.Benchmarks.Runtime.ServerExtensionProxyLookupProbe.Run();
    return;
}

if (args.Contains("--probe-literal-scalar-safety", StringComparer.OrdinalIgnoreCase)) {
    DotBoxD.Kernels.Benchmarks.Validation.LiteralScalarSafetyProbe.Run();
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

using System.Globalization;
using BenchmarkDotNet.Running;
using DotBoxD.Kernels.Benchmarks.Ipc;

if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
{
    await IpcAllocationSmoke.RunAsync();
    return;
}

if (args.Contains("--probe-compiled", StringComparer.OrdinalIgnoreCase))
{
    await DotBoxD.Kernels.Benchmarks.Interpreter.CompiledSpeedProbe.RunAsync();
    return;
}

if (args.Contains("--probe-bindings", StringComparer.OrdinalIgnoreCase))
{
    await DotBoxD.Kernels.Benchmarks.Interpreter.BindingCrossingProbe.RunAsync();
    return;
}

if (args.Contains("--probe-matrix", StringComparer.OrdinalIgnoreCase))
{
    await DotBoxD.Kernels.Benchmarks.Interpreter.PerformanceMatrixProbe.RunAsync();
    return;
}

if (args.Contains("--probe-branched-f64-loop", StringComparer.OrdinalIgnoreCase))
{
    await DotBoxD.Kernels.Benchmarks.Interpreter.BranchedF64LoopProbe.RunAsync();
    return;
}

if (args.Contains("--probe-rogue", StringComparer.OrdinalIgnoreCase))
{
    await DotBoxD.Kernels.Benchmarks.Interpreter.RogueScalingProbe.RunAsync();
    return;
}

if (args.Contains("--probe-examples", StringComparer.OrdinalIgnoreCase))
{
    await DotBoxD.Kernels.Benchmarks.Examples.ExampleWorkflowProbe.RunAsync();
    return;
}

if (args.Contains("--probe-prepared-values", StringComparer.OrdinalIgnoreCase))
{
    await DotBoxD.Kernels.Benchmarks.Examples.PreparedValueProbe.RunAsync();
    return;
}

if (args.Contains("--probe-runtime-types", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Runtime.RuntimeTypeProbe.Run();
    return;
}

if (args.Contains("--probe-resource-meter", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Runtime.ResourceMeterProbe.Run();
    return;
}

if (DotBoxD.Kernels.Benchmarks.Http.HttpProbeDispatcher.TryRun(args))
{
    return;
}

if (args.Contains("--probe-safe-file-path-safety", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.File.SafeFilePathSafetyProbe.Run();
    return;
}

if (args.Contains("--probe-value-shape-cache", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Runtime.ValueShapeCacheProbe.Run();
    return;
}

if (args.Contains("--probe-validated-value-type", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Validation.ValidatedValueTypeProbe.Run();
    return;
}

if (args.Contains("--probe-empty-validated-value", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Validation.EmptyStructuralValidationProbe.Run();
    return;
}

if (args.Contains("--probe-nonempty-structural-validation", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Validation.NonEmptyStructuralValidationProbe.Run();
    return;
}

if (args.Contains("--probe-compiled-binding-fast-path", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Runtime.CompiledBindingFastPathProbe.Run();
    return;
}

if (args.Contains("--probe-compiled-binding-structural-validation", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Runtime.CompiledBindingStructuralValidationProbe.Run();
    return;
}

if (args.Contains("--probe-i32-math-intrinsic", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Runtime.I32MathIntrinsicProbe.Run();
    return;
}

if (args.Contains("--probe-f64-math-intrinsic", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Runtime.F64MathIntrinsicProbe.Run();
    return;
}

if (args.Contains("--probe-raw-unary-negation", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Runtime.RawUnaryNegationProbe.Run();
    return;
}

if (args.Contains("--probe-numeric-conversion", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Runtime.NumericConversionProbe.Run();
    return;
}

if (args.Contains("--probe-binding-return-credit", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Runtime.BindingReturnCreditProbe.Run();
    return;
}

if (args.Contains("--probe-binding-registry", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Core.BindingRegistryProbe.Run();
    return;
}

if (args.Contains("--probe-map-remove", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Core.MapRemoveProbe.Run();
    return;
}

if (args.Contains("--probe-map-set-replace", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Core.MapSetReplaceProbe.Run();
    return;
}

if (args.Contains("--probe-list-add-type-match", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Core.ListAddTypeMatchProbe.Run();
    return;
}

if (args.Contains("--probe-collection-construction", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Core.CollectionConstructionProbe.Run();
    return;
}

if (args.Contains("--probe-literal-collection-construction", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Core.LiteralCollectionConstructionProbe.Run();
    return;
}

if (args.Contains("--probe-host-call-accounting", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Runtime.HostCallAccountingProbe.Run();
    return;
}

if (args.Contains("--probe-run-summary-policy-id", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Runtime.Audit.RunSummaryPolicyIdProbe.Run();
    return;
}

if (args.Contains("--probe-binding-dispatch-scope", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Runtime.BindingDispatchScopeProbe.Run();
    return;
}

if (args.Contains("--probe-compiled-binding-arity", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Runtime.CompiledBindingArityProbe.Run();
    return;
}

if (args.Contains("--probe-capability-grant-lookup", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Runtime.CapabilityGrantLookupProbe.Run();
    return;
}

if (args.Contains("--probe-server-extension-proxy-lookup", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Runtime.ServerExtensionProxyLookupProbe.Run();
    return;
}

if (args.Contains("--probe-installed-rpc-input", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Runtime.InstalledRpcInputProbe.Run();
    return;
}

if (DotBoxD.Kernels.Benchmarks.Runtime.KernelRpcProbeDispatcher.TryRun(args))
{
    return;
}

if (args.Contains("--probe-runlocal-push", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Ipc.RunLocal.RunLocalPushProbe.Run();
    return;
}

if (args.Contains("--probe-remote-result-hook", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Plugins.RemoteResultHookProbe.Run();
    return;
}

if (args.Contains("--probe-subscription-dispatch", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Plugins.SubscriptionDispatchProbe.Run();
    return;
}

if (args.Contains("--probe-hook-dispatch", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Plugins.HookDispatchProbe.Run();
    return;
}

if (args.Contains("--probe-event-query-dispatch", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Queryable.EventQueryDispatchProbe.Run();
    return;
}

if (args.Contains("--probe-json-schema-resources", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Json.JsonSchemaResourceProbe.Run();
    return;
}

if (args.Contains("--probe-json-import-source-map", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Json.JsonImportSourceMapProbe.Run();
    return;
}

if (args.Contains("--probe-literal-scalar-safety", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Validation.LiteralScalarSafetyProbe.Run();
    return;
}

if (args.Contains("--probe-sandbox-type-validation", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Validation.SandboxTypeValidationProbe.Run();
    return;
}

if (args.Contains("--probe-verifier-opcode-branches", StringComparer.OrdinalIgnoreCase))
{
    DotBoxD.Kernels.Benchmarks.Verifier.GeneratedVerifierOpcodeProbe.Run();
    return;
}

var profileIndex = Array.FindIndex(args, arg => arg.Equals("--profile-ipc", StringComparison.OrdinalIgnoreCase));
if (profileIndex >= 0)
{
    var transport = args.ElementAtOrDefault(profileIndex + 1) ?? IpcAllocationProfile.NamedPipeTransport;
    var iterationsText = args.ElementAtOrDefault(profileIndex + 2) ?? "10000";
    var iterations = int.Parse(iterationsText, CultureInfo.InvariantCulture);
    var disableTimeout = args.Contains("--no-timeout", StringComparer.OrdinalIgnoreCase);
    var lowAllocationProfile = args.Contains("--low-alloc", StringComparer.OrdinalIgnoreCase);
    await IpcAllocationProfile.RunAsync(transport, iterations, disableTimeout, lowAllocationProfile);
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

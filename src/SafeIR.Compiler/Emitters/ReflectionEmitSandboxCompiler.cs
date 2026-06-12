namespace SafeIR.Compiler.Emitters;

using System.Reflection;
using System.Reflection.Emit;
using SafeIR;
using SafeIR.Runtime;
using SafeIR.Verifier;
using static SafeIR.Compiler.IlEmitterPrimitives;

public sealed class ReflectionEmitSandboxCompiler : ISandboxCompiler
{
    private readonly IGeneratedAssemblyVerifier _verifier;
    private readonly VerificationPolicy _verificationPolicy;
    private readonly PersistentCompiledArtifactCache? _cache;

    public ReflectionEmitSandboxCompiler(
        IGeneratedAssemblyVerifier verifier,
        VerificationPolicy? verificationPolicy = null,
        PersistentCompiledArtifactCache? cache = null)
    {
        _verifier = verifier;
        _verificationPolicy = verificationPolicy ?? VerificationPolicy.BoxedValueDefaults();
        _cache = cache;
    }

    public async ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken)
    {
        var function = ResolveSupportedFunction(plan, options.Entrypoint);
        var cacheKey = CacheKeyBuilder.Build(plan, options.Entrypoint, _verificationPolicy, options.Optimize);
        var lookupStatus = CompiledCacheStatus.None;
        string? cacheInvalidReason = null;
        if (_cache is not null)
        {
            var cached = await _cache.TryReadAsync(
                cacheKey,
                plan,
                options.Entrypoint,
                _verifier,
                _verificationPolicy,
                cancellationToken).ConfigureAwait(false);
            if (cached.Status == CompiledCacheStatus.Hit && cached.Artifact is not null)
            {
                return cached.Artifact with
                {
                    Entrypoint = UnmaterializedEntrypoint,
                    CacheStatus = CompiledCacheStatus.Hit
                };
            }

            lookupStatus = cached.Status;
            cacheInvalidReason = cached.InvalidReason;
        }

        var assemblyBytes = EmitAssembly(plan, function);
        var manifest = BuildManifest(plan, assemblyBytes, options);
        var verificationPolicy = _verificationPolicy.WithExpectedManifest(
            CacheKeyBuilder.BuildManifestIdentity(plan, options.Entrypoint, _verificationPolicy, options.Optimize));
        var verification = await _verifier.VerifyAsync(assemblyBytes, manifest, verificationPolicy, cancellationToken)
            .ConfigureAwait(false);
        if (!verification.Succeeded)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.VerifierFailure,
                VerificationFailureMessage(verification)));
        }

        var status = _cache is null
            ? CompiledCacheStatus.None
            : await WriteCacheAsync(
                    cacheKey,
                    lookupStatus,
                    plan,
                    options.Entrypoint,
                    assemblyBytes,
                    manifest,
                    verification,
                    cancellationToken)
                .ConfigureAwait(false);
        return new CompiledArtifact(
            assemblyBytes,
            verification.AssemblyHash,
            manifest,
            verification,
            UnmaterializedEntrypoint,
            CompiledRuntimeFormKind.LoadedAssembly,
            status,
            lookupStatus == CompiledCacheStatus.Invalid ? cacheInvalidReason : null);
    }

    private static SandboxFunction ResolveSupportedFunction(ExecutionPlan plan, string entrypoint)
    {
        var function = plan.Module.Functions.FirstOrDefault(f => f.Id == entrypoint && f.IsEntrypoint);
        if (function is null)
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"entrypoint '{entrypoint}' is not available"));
        }

        var effects = plan.FunctionAnalysis[function.Id].Effects;
        if ((effects & ~(SandboxEffect.Cpu | SandboxEffect.Alloc)) != SandboxEffect.None)
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "compiled mode supports pure modules only"));
        }

        return function;
    }

    private static byte[] EmitAssembly(ExecutionPlan plan, SandboxFunction function)
    {
        var assemblyName = new AssemblyName("SafeIR.Generated." + plan.ModuleHash[..16]);
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("SafeIR.Generated.Module");
        var type = module.DefineType("SafeIR.Generated.Module_" + plan.ModuleHash[..16], TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract);
        var reachableFunctions = ReachableFunctionCollector.Collect(plan, function);
        var functions = DefineFunctionMethods(type, reachableFunctions);
        var methodReferences = functions.ToDictionary(p => p.Key, p => (MethodInfo)p.Value, StringComparer.Ordinal);
        var execute = type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);

        EmitExecute(execute.GetILGenerator(), function, functions[function.Id]);
        foreach (var item in reachableFunctions)
        {
            new MethodEmitter(
                functions[item.Id].GetILGenerator(),
                item,
                methodReferences,
                plan.Bindings,
                plan.FunctionAnalysis).Emit();
        }

        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    private static Dictionary<string, MethodBuilder> DefineFunctionMethods(
        TypeBuilder type,
        IReadOnlyList<SandboxFunction> functions)
    {
        var methods = new Dictionary<string, MethodBuilder>(StringComparer.Ordinal);
        for (var i = 0; i < functions.Count; i++)
        {
            var function = functions[i];
            methods[function.Id] = type.DefineMethod(
                "Fn_" + i,
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                FunctionParameterTypes(function));
        }

        return methods;
    }

    private static Type[] FunctionParameterTypes(SandboxFunction function)
    {
        var parameters = new Type[function.Parameters.Count + 1];
        parameters[0] = typeof(SandboxContext);
        Array.Fill(parameters, typeof(SandboxValue), 1, function.Parameters.Count);
        return parameters;
    }

    private static void EmitExecute(ILGenerator il, SandboxFunction entrypoint, MethodInfo entrypointMethod)
    {
        il.Emit(OpCodes.Ldarg_1);
        EmitInt32(il, entrypoint.Parameters.Count);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ValidateEntrypointInput)));

        il.Emit(OpCodes.Ldarg_0);
        for (var i = 0; i < entrypoint.Parameters.Count; i++)
        {
            il.Emit(OpCodes.Ldarg_1);
            EmitInt32(il, i);
            EmitInt32(il, entrypoint.Parameters.Count);
            EmitSandboxType(il, entrypoint.Parameters[i].Type);
            il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.GetInputArgument)));
        }

        il.Emit(OpCodes.Call, entrypointMethod);
        il.Emit(OpCodes.Ret);
    }

    private ArtifactManifest BuildManifest(ExecutionPlan plan, byte[] assemblyBytes, CompileOptions options)
    {
        var assemblyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(assemblyBytes)).ToLowerInvariant();
        return new ArtifactManifest(
            1,
            CacheKeyBuilder.Build(plan, options.Entrypoint, _verificationPolicy, options.Optimize),
            plan.ModuleHash,
            plan.PlanHash,
            plan.PolicyHash,
            plan.BindingManifestHash,
            _verificationPolicy.RuntimeFacadeHash,
            CacheKeyBuilder.CompilerVersion,
            CacheKeyBuilder.TypeSystemVersion,
            CacheKeyBuilder.EffectAnalysisVersion,
            _verificationPolicy.VerifierVersion,
            CacheKeyBuilder.LanguageVersion,
            CacheKeyBuilder.TargetFramework,
            [options.Optimize ? "opt" : "boxed-values"],
            assemblyHash,
            DateTimeOffset.UtcNow);
    }

    private async ValueTask<CompiledCacheStatus> WriteCacheAsync(
        string cacheKey,
        CompiledCacheStatus lookupStatus,
        ExecutionPlan plan,
        string entrypoint,
        byte[] assemblyBytes,
        ArtifactManifest manifest,
        VerificationResult verification,
        CancellationToken cancellationToken)
    {
        var cache = _cache ?? throw new InvalidOperationException("compiler cache is not configured");
        var existing = lookupStatus == CompiledCacheStatus.Invalid || cache.EntryExists(cacheKey)
            ? CompiledCacheStatus.Recompiled
            : CompiledCacheStatus.Miss;
        await cache.WriteAsync(cacheKey, plan, entrypoint, assemblyBytes, manifest, verification, _verificationPolicy, cancellationToken)
            .ConfigureAwait(false);
        return existing;
    }

    private static string VerificationFailureMessage(VerificationResult verification)
    {
        if (verification.Diagnostics.Count == 0)
        {
            return "compiled artifact failed verification";
        }

        var diagnostics = string.Join(
            "; ",
            verification.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));
        return $"compiled artifact failed verification: {diagnostics}";
    }

    private static SandboxValue UnmaterializedEntrypoint(SandboxContext _, SandboxValue __)
        => throw new InvalidOperationException("loaded assembly artifacts are materialized by the host");
}

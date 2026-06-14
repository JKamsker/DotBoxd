using System.Reflection;
using System.Reflection.Emit;
using SafeIR.Runtime;
using SafeIR.Verifier;

namespace SafeIR.Tests;

internal static class VerifierTestHelpers
{
    public static async ValueTask<VerificationResult> VerifyAsync(byte[] bytes)
        => await VerifyAsync(bytes, VerificationPolicy.BoxedValueDefaults());

    public static async ValueTask<VerificationResult> VerifyAsync(byte[] bytes, VerificationPolicy policy)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var manifest = new ArtifactManifest(
            1,
            "test",
            "module",
            "plan",
            "policy",
            "bindings",
            "runtime",
            "compiler",
            "type-system",
            "effect-analysis",
            "verifier",
            "1.0.0",
            "net10.0",
            [],
            hash,
            DateTimeOffset.UtcNow);

        var verificationPolicy = policy.ExpectedManifestIdentity is null
            ? policy.WithExpectedManifest(VerificationManifestIdentity.FromManifest(manifest))
            : policy;
        return await new GeneratedAssemblyVerifier()
            .VerifyAsync(bytes, manifest, verificationPolicy, CancellationToken.None);
    }

    public static byte[] BuildGeneratedAssembly(Action<TypeBuilder> define)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("Generated" + Guid.NewGuid().ToString("N")),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("GeneratedModule");
        var type = module.DefineType(
            "SafeIR.Generated.Module_0123456789abcdef",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        define(type);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    public static MethodBuilder DefineValidExecute(TypeBuilder type)
    {
        var fn = type.DefineMethod(
            "Fn_0",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext)]);
        var fnIl = fn.GetILGenerator();
        var value = fnIl.DeclareLocal(typeof(SandboxValue));
        EmitEnterCall(fnIl);
        EmitChargeFuel(fnIl);
        fnIl.Emit(OpCodes.Ldc_I4_0);
        fnIl.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!);
        fnIl.Emit(OpCodes.Stloc, value);
        EmitExitCall(fnIl);
        fnIl.Emit(OpCodes.Ldloc, value);
        fnIl.Emit(OpCodes.Ret);

        var method = type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ValidateEntrypointInput))!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, fn);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static void EmitEnterCall(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.EnterCall))!);
    }

    private static void EmitChargeFuel(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ChargeFuel))!);
    }

    private static void EmitExitCall(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ExitCall))!);
    }
}

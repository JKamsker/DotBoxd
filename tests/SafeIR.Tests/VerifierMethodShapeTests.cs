using System.Reflection;
using System.Reflection.Emit;
using SafeIR.Runtime;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class VerifierMethodShapeTests
{
    [Fact]
    public async Task Verifier_rejects_module_level_methods()
    {
        var result = await VerifyAsync(AssemblyWithGlobalMethod());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-MODULE-SURFACE");
    }

    [Theory]
    [InlineData("Helper")]
    [InlineData("Fn_alpha")]
    [InlineData("Fn_")]
    public async Task Verifier_rejects_unexpected_generated_method_names(string methodName)
    {
        var result = await VerifyAsync(AssemblyWithHelper(methodName));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-METHOD-NAME");
    }

    [Fact]
    public async Task Verifier_rejects_local_generated_call_before_meters()
    {
        var result = await VerifyAsync(AssemblyWithUnmeteredRecursiveCall());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains("before local calls", StringComparison.Ordinal));
    }

    private static async ValueTask<VerificationResult> VerifyAsync(byte[] bytes)
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

        return await new GeneratedAssemblyVerifier()
            .VerifyAsync(bytes, manifest, VerificationPolicy.BoxedValueDefaults(), CancellationToken.None);
    }

    private static byte[] AssemblyWithHelper(string methodName)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("Methoded" + Guid.NewGuid().ToString("N")),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("MethodedModule");
        var type = module.DefineType(
            "SafeIR.Generated.Module_0123456789abcdef",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        DefineVoidMethod(type, "Execute", MethodAttributes.Public | MethodAttributes.Static);
        DefineVoidMethod(type, methodName, MethodAttributes.Private | MethodAttributes.Static);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    private static byte[] AssemblyWithGlobalMethod()
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("GlobalMethoded" + Guid.NewGuid().ToString("N")),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("GlobalMethodedModule");
        var global = module.DefineGlobalMethod(
            "Global_0",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            []);
        global.GetILGenerator().Emit(OpCodes.Ret);
        module.CreateGlobalFunctions();
        var type = module.DefineType(
            "SafeIR.Generated.Module_0123456789abcdef",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        VerifierTestHelpers.DefineValidExecute(type);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    private static byte[] AssemblyWithUnmeteredRecursiveCall()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var function = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext)]);
            var fnIl = function.GetILGenerator();
            fnIl.Emit(OpCodes.Ldarg_0);
            fnIl.Emit(OpCodes.Call, function);
            fnIl.Emit(OpCodes.Pop);
            fnIl.Emit(OpCodes.Ldarg_0);
            fnIl.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.EnterCall))!);
            fnIl.Emit(OpCodes.Ldarg_0);
            fnIl.Emit(OpCodes.Ldc_I4_1);
            fnIl.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ChargeFuel))!);
            fnIl.Emit(OpCodes.Ldarg_0);
            fnIl.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ExitCall))!);
            fnIl.Emit(OpCodes.Ldc_I4_0);
            fnIl.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!);
            fnIl.Emit(OpCodes.Ret);

            var execute = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            var il = execute.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ValidateEntrypointInput))!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, function);
            il.Emit(OpCodes.Ret);
        });

    private static void DefineVoidMethod(TypeBuilder type, string name, MethodAttributes attributes)
    {
        var method = type.DefineMethod(name, attributes, typeof(void), []);
        method.GetILGenerator().Emit(OpCodes.Ret);
    }
}

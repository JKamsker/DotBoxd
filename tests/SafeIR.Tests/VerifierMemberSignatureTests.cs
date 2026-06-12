using System.Reflection;
using System.Reflection.Emit;
using SafeIR.Runtime;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class VerifierMemberSignatureTests
{
    [Fact]
    public async Task Verifier_rejects_name_only_member_allowlist_entries()
    {
        var policy = VerificationPolicy.BoxedValueDefaults() with {
            AllowedMembers = new HashSet<string>(StringComparer.Ordinal) {
                "SafeIR.Runtime.CompiledRuntime.I32"
            }
        };
        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type => {
            var method = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!);
            il.Emit(OpCodes.Ret);
        });

        var result = await VerifierTestHelpers.VerifyAsync(bytes, policy);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-MEMBER");
    }

    [Fact]
    public async Task Verifier_rejects_spoofed_runtime_assembly_identity()
    {
        var fakeRuntimeI32 = FakeRuntimeMethod();
        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type => {
            var fn = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext)]);
            var fnIl = fn.GetILGenerator();
            EmitRuntimeCall(fnIl, nameof(CompiledRuntime.EnterCall));
            EmitFuel(fnIl);
            fnIl.Emit(OpCodes.Ldc_I4_1);
            fnIl.Emit(OpCodes.Call, fakeRuntimeI32);
            var value = fnIl.DeclareLocal(typeof(SandboxValue));
            fnIl.Emit(OpCodes.Stloc, value);
            EmitRuntimeCall(fnIl, nameof(CompiledRuntime.ExitCall));
            fnIl.Emit(OpCodes.Ldloc, value);
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
            il.Emit(OpCodes.Call, fn);
            il.Emit(OpCodes.Ret);
        });

        var result = await VerifierTestHelpers.VerifyAsync(bytes);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-ASM-REF");
    }

    [Fact]
    public async Task Verifier_rejects_assembly_reference_name_missing_from_allowlist()
    {
        var defaultPolicy = VerificationPolicy.BoxedValueDefaults();
        var policy = defaultPolicy with
        {
            AllowedAssemblies = defaultPolicy.AllowedAssemblies
                .Where(name => !string.Equals(name, "SafeIR.Core", StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal)
        };
        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type => VerifierTestHelpers.DefineValidExecute(type));

        var result = await VerifierTestHelpers.VerifyAsync(bytes, policy);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-ASM-REF" &&
            d.Message.Contains("SafeIR.Core", StringComparison.Ordinal));
    }

    private static MethodInfo FakeRuntimeMethod()
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("SafeIR.Runtime") { Version = new Version(9, 9, 9, 9) },
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("FakeRuntime");
        var type = module.DefineType(
            "SafeIR.Runtime.CompiledRuntime",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var method = type.DefineMethod(
            "I32",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(int)]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        type.CreateType();
        return method;
    }

    private static void EmitRuntimeCall(ILGenerator il, string method)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(method)!);
    }

    private static void EmitFuel(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ChargeFuel))!);
    }
}

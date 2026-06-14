using System.Reflection;
using System.Reflection.Emit;
using SafeIR.Runtime;
using SafeIR.Verifier;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for PAL-0017: member signatures are now decoded once per
/// metadata token during IL reading and reused by the opcode verifier instead of
/// being re-decoded on every traversal. These tests assert the observable
/// verification behavior is unchanged when the same call token appears at many
/// call sites.
/// </summary>
public sealed class Fix_PAL_0017_Tests
{
    private const int RepeatedCallCount = 64;

    [Fact]
    public async Task Disallowed_member_called_many_times_still_reports_single_member_diagnostic()
    {
        // Restrict the allowlist so CompiledRuntime.I32 is not allowed, then call it
        // many times with the same metadata token. The repeated decode path must
        // produce the identical V-MEMBER diagnostic it produced before the fix.
        var policy = VerificationPolicy.BoxedValueDefaults() with
        {
            AllowedMembers = new HashSet<string>(StringComparer.Ordinal)
            {
                "SafeIR.Runtime.CompiledRuntime.I32"
            }
        };

        var i32 = typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!;
        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var execute = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            var il = execute.GetILGenerator();
            for (var i = 0; i < RepeatedCallCount; i++)
            {
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Call, i32);
                il.Emit(OpCodes.Pop);
            }

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
        });

        var result = await VerifierTestHelpers.VerifyAsync(bytes, policy);

        Assert.False(result.Succeeded);
        var memberDiagnostics = result.Diagnostics
            .Where(d => d.Code == "V-MEMBER")
            .Select(d => d.Message)
            .ToArray();
        Assert.NotEmpty(memberDiagnostics);
        Assert.All(memberDiagnostics, message =>
            Assert.Contains("CompiledRuntime.I32", message, StringComparison.Ordinal));
        // The same disallowed token resolves to one signature string regardless of
        // how many call sites reference it.
        Assert.Single(memberDiagnostics.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Allowed_member_called_many_times_never_reports_member_diagnostic()
    {
        // Repeated calls to an allowed helper share one cached signature; the cache
        // must not corrupt the allowlist decision for any of the repeated tokens.
        var i32 = typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!;
        var enterCall = typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.EnterCall))!;
        var exitCall = typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ExitCall))!;
        var chargeFuel = typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ChargeFuel))!;
        var validate = typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ValidateEntrypointInput))!;

        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext)]);
            var fnIl = fn.GetILGenerator();
            var value = fnIl.DeclareLocal(typeof(SandboxValue));
            fnIl.Emit(OpCodes.Ldarg_0);
            fnIl.Emit(OpCodes.Call, enterCall);
            fnIl.Emit(OpCodes.Ldarg_0);
            fnIl.Emit(OpCodes.Ldc_I4_1);
            fnIl.Emit(OpCodes.Call, chargeFuel);
            for (var i = 0; i < RepeatedCallCount; i++)
            {
                fnIl.Emit(OpCodes.Ldc_I4_1);
                fnIl.Emit(OpCodes.Call, i32);
                fnIl.Emit(OpCodes.Stloc, value);
            }

            fnIl.Emit(OpCodes.Ldarg_0);
            fnIl.Emit(OpCodes.Call, exitCall);
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
            il.Emit(OpCodes.Call, validate);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, fn);
            il.Emit(OpCodes.Ret);
        });

        var result = await VerifierTestHelpers.VerifyAsync(bytes);

        // The repeated allowed call must never be flagged as a disallowed member.
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "V-MEMBER");
    }
}

using System.Reflection;
using System.Reflection.Emit;
using SafeIR.Runtime;
using SafeIR.Verifier;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for PAL-0025: the generated-method flow analyzer used to allocate a
/// per-instruction successor array (<c>Successors(...).ToArray()</c>) and then recompute
/// successors again inside reachability and cycle detection instead of reusing the already
/// materialized successor map.
///
/// The fix stores successors in a compact, allocation-free <c>SuccessorSet</c> (inline storage
/// for the common zero/one/two-successor shapes) and routes reachability and cycle detection
/// through that single materialized map. These tests pin the observable behavior that must be
/// preserved across the refactor: control-flow edges for linear fallthrough, converging
/// branches, and back-edges (loops) must be discovered identically, so verification outcomes
/// do not change.
/// </summary>
public sealed class Fix_PAL_0025_Tests
{
    [Fact]
    public async Task Linear_fallthrough_method_still_verifies()
    {
        // Exercises the zero/one-successor (fallthrough + ret) path through the materialized
        // successor map and the reachability/meter traversals that consume it.
        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type => VerifierTestHelpers.DefineValidExecute(type));

        var result = await VerifierTestHelpers.VerifyAsync(bytes);

        Assert.True(result.Succeeded, FormatDiagnostics(result));
    }

    [Fact]
    public async Task Converging_branch_method_still_verifies()
    {
        // A conditional branch produces a two-successor instruction (target + fallthrough),
        // the inline storage case. Both arms converge at the same offset with an equal stack
        // height, so the flow analysis must still report the merge as reachable and consistent.
        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            var il = fn.GetILGenerator();
            var merge = il.DefineLabel();
            EmitEnterCall(il);
            EmitChargeFuel(il);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Brfalse_S, merge); // two successors: merge target and fallthrough
            EmitChargeFuel(il);                // fallthrough arm does a little extra work
            il.MarkLabel(merge);               // both arms converge here at stack depth 0
            EmitExitCall(il);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);

            EmitExecuteCalling(type, fn);
        });

        var result = await VerifierTestHelpers.VerifyAsync(bytes);

        Assert.True(result.Succeeded, FormatDiagnostics(result));
    }

    [Fact]
    public async Task Unmetered_loop_back_edge_is_still_rejected()
    {
        // A back-edge (Brtrue_S to an earlier label) is the case where cycle detection must see
        // the successor edge through the materialized map. An unmetered loop must still be
        // flagged; if the refactored cycle detection lost the back-edge, this would pass instead.
        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            var il = fn.GetILGenerator();
            var loop = il.DefineLabel();
            EmitEnterCall(il);
            EmitChargeFuel(il);
            il.MarkLabel(loop);
            EmitChargeFuel(il);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Brtrue_S, loop); // back-edge: unmetered loop
            EmitExitCall(il);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);

            EmitExecuteCalling(type, fn);
        });

        var result = await VerifierTestHelpers.VerifyAsync(bytes);

        Assert.False(result.Succeeded, "An unmetered loop back-edge must be rejected.");
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains("loop iterations", StringComparison.Ordinal));
    }

    private static void EmitExecuteCalling(TypeBuilder type, MethodInfo function)
    {
        var il = type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]).GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.ValidateEntrypointInput)));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, function);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitEnterCall(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.EnterCall)));
    }

    private static void EmitChargeFuel(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.ChargeFuel)));
    }

    private static void EmitExitCall(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.ExitCall)));
    }

    private static MethodInfo RuntimeMethod(string name)
        => typeof(CompiledRuntime).GetMethod(name) ?? throw new MissingMethodException(nameof(CompiledRuntime), name);

    private static string FormatDiagnostics(VerificationResult result)
        => result.Succeeded
            ? string.Empty
            : "Verification failed: " + string.Join(
                "; ",
                result.Diagnostics.Select(d => $"{d.Code}:{d.Message}"));
}

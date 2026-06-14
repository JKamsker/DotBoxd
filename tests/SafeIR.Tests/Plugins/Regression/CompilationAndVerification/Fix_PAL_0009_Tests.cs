using System.Reflection;
using System.Reflection.Emit;
using SafeIR.Runtime;

namespace SafeIR.Tests;

// Regression coverage for PAL-0009: GeneratedStackTypeVerifier used to start the
// transfer of every reachable instruction with `input.ToList()`, cloning the whole
// symbolic operand stack once per instruction in addition to the immutable snapshot
// it already stores per control-flow state. The fix reuses a single mutable working
// buffer for the transfer and keeps the stored per-offset snapshots immutable, so the
// throwaway per-instruction clone is removed.
//
// The risk in that refactor is correctness: because one buffer is now shared across
// every instruction, a bug could let the working buffer alias or corrupt the stored
// per-offset snapshots, or leak operand state from one offset into another. These
// tests drive the real public verifier through a generated method body and assert on
// exactly the stack-type / stack-height diagnostics the optimized verifier produces.
// They deliberately do NOT assert overall verification success, because a hand-emitted
// helper trips unrelated metering-shape rules; they assert only the V-STACK* signal
// that PAL-0009 must preserve EXACTLY.
public sealed class Fix_PAL_0009_Tests
{
    private const int DeepStackDepth = 256;

    [Fact]
    public async Task Deep_transient_operand_stack_produces_no_spurious_stack_diagnostics()
    {
        var result = await VerifierTestHelpers.VerifyAsync(WithHelper(EmitDeepBalancedStack));

        // The helper builds the symbolic stack to DeepStackDepth with matching Int32
        // values and drains it back to a single returned value. The reused working
        // buffer must be refilled correctly for every instruction, so neither a false
        // underflow (V-STACK) nor a false type mismatch (V-STACK-TYPE) may appear.
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "V-STACK");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "V-STACK-TYPE");
    }

    [Fact]
    public async Task Type_inconsistent_branch_merge_is_still_flagged()
    {
        var result = await VerifierTestHelpers.VerifyAsync(WithHelper(EmitTypeInconsistentMerge));

        // Both branch paths reach the join at the same height but with different
        // operand types (Int32 vs SandboxValue). The merge comparison between the
        // stored snapshot and the freshly computed working buffer must still detect
        // the divergence, proving the stored snapshot stays independent of the
        // reused buffer.
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-STACK-TYPE" &&
            d.Message.Contains("inconsistent stack types", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Type_consistent_branch_merge_is_not_flagged()
    {
        var result = await VerifierTestHelpers.VerifyAsync(WithHelper(EmitTypeConsistentMerge));

        // Both branch paths reach the join with the same operand type, so the reused
        // buffer must not produce a spurious inconsistency at the merge.
        Assert.DoesNotContain(result.Diagnostics, d =>
            d.Code == "V-STACK-TYPE" &&
            d.Message.Contains("inconsistent stack types", StringComparison.Ordinal));
    }

    // Pushes DeepStackDepth Int32 constants, growing the symbolic stack, then pops them
    // all and returns a single value. This is exactly the per-instruction transfer at
    // large, varying stack depth where the old per-instruction clone piled up.
    private static void EmitDeepBalancedStack(ILGenerator il)
    {
        for (var i = 0; i < DeepStackDepth; i++)
        {
            il.Emit(OpCodes.Ldc_I4_0);
        }

        for (var i = 0; i < DeepStackDepth; i++)
        {
            il.Emit(OpCodes.Pop);
        }

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitTypeInconsistentMerge(ILGenerator il)
    {
        var typed = il.DefineLabel();
        var join = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Brtrue_S, typed);
        il.Emit(OpCodes.Ldc_I4_0);      // fallthrough leaves Int32 at the join
        il.Emit(OpCodes.Br_S, join);
        il.MarkLabel(typed);
        il.Emit(OpCodes.Ldarg_1);       // branch path leaves SandboxValue at the join
        il.MarkLabel(join);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitTypeConsistentMerge(ILGenerator il)
    {
        var typed = il.DefineLabel();
        var join = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Brtrue_S, typed);
        il.Emit(OpCodes.Ldarg_1);       // both paths leave SandboxValue at the join
        il.Emit(OpCodes.Br_S, join);
        il.MarkLabel(typed);
        il.Emit(OpCodes.Ldarg_1);
        il.MarkLabel(join);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);
    }

    // Builds a structurally valid generated assembly with a real Execute method and an
    // extra generated helper whose body carries the operand-stack scenario under test.
    // Every method body, including this helper, is run through GeneratedStackTypeVerifier.
    private static byte[] WithHelper(Action<ILGenerator> emitHelperBody)
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            VerifierTestHelpers.DefineValidExecute(type);
            var helper = type.DefineMethod(
                "Fn_7",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            emitHelperBody(helper.GetILGenerator());
        });
}

using DotBoxD.Kernels.Verifier.Generated;
using DotBoxD.Kernels.Verifier.Generated.Methods;

namespace DotBoxD.Kernels.Verifier;

using System.Reflection;
using System.Reflection.Metadata;

internal static class OpCodeVerifier
{
    private static readonly HashSet<ILOpCode> Allowed = [
        ILOpCode.Nop, ILOpCode.Ldarg_0, ILOpCode.Ldarg_1, ILOpCode.Ldarg_2, ILOpCode.Ldarg_3,
        ILOpCode.Ldarg, ILOpCode.Ldarg_s,
        ILOpCode.Ldloc_0, ILOpCode.Ldloc_1, ILOpCode.Ldloc_2, ILOpCode.Ldloc_3,
        ILOpCode.Ldloc, ILOpCode.Ldloc_s, ILOpCode.Stloc_0, ILOpCode.Stloc_1, ILOpCode.Stloc_2, ILOpCode.Stloc_3,
        ILOpCode.Stloc, ILOpCode.Stloc_s, ILOpCode.Ldnull, ILOpCode.Ldc_i4, ILOpCode.Ldc_i4_s,
        ILOpCode.Ldc_i4_0, ILOpCode.Ldc_i4_1, ILOpCode.Ldc_i4_2, ILOpCode.Ldc_i4_3, ILOpCode.Ldc_i4_4,
        ILOpCode.Ldc_i4_5, ILOpCode.Ldc_i4_6, ILOpCode.Ldc_i4_7, ILOpCode.Ldc_i4_8, ILOpCode.Ldc_i4_m1,
        ILOpCode.Ldc_i8, ILOpCode.Ldc_r8, ILOpCode.Ldstr, ILOpCode.Br, ILOpCode.Br_s, ILOpCode.Brtrue,
        ILOpCode.Brtrue_s,
        ILOpCode.Brfalse, ILOpCode.Brfalse_s, ILOpCode.Beq, ILOpCode.Beq_s, ILOpCode.Bne_un, ILOpCode.Bne_un_s,
        ILOpCode.Blt, ILOpCode.Blt_s, ILOpCode.Bgt, ILOpCode.Bgt_s, ILOpCode.Ble, ILOpCode.Ble_s,
        ILOpCode.Bge, ILOpCode.Bge_s, ILOpCode.Add, ILOpCode.Sub, ILOpCode.Mul, ILOpCode.Div,
        ILOpCode.Rem, ILOpCode.Neg, ILOpCode.And, ILOpCode.Or, ILOpCode.Xor, ILOpCode.Not,
        ILOpCode.Ceq, ILOpCode.Clt, ILOpCode.Cgt, ILOpCode.Conv_i8, ILOpCode.Conv_r8, ILOpCode.Call,
        ILOpCode.Ret, ILOpCode.Pop, ILOpCode.Dup, ILOpCode.Stelem_ref
    ];

    private static readonly HashSet<ILOpCode> Forbidden = [
        ILOpCode.Calli, ILOpCode.Jmp, ILOpCode.Localloc, ILOpCode.Cpblk, ILOpCode.Initblk,
        ILOpCode.Ldftn, ILOpCode.Ldvirtftn, ILOpCode.Ldtoken, ILOpCode.Mkrefany,
        ILOpCode.Refanytype, ILOpCode.Refanyval, ILOpCode.Arglist, ILOpCode.Throw,
        ILOpCode.Rethrow, ILOpCode.Box, ILOpCode.Unbox, ILOpCode.Unbox_any,
        ILOpCode.Castclass, ILOpCode.Isinst, ILOpCode.Ldsfld, ILOpCode.Stsfld,
        ILOpCode.Starg, ILOpCode.Starg_s
    ];

    public static void VerifyBody(
        MetadataReader reader,
        VerificationPolicy policy,
        MethodBodyBlock body,
        IReadOnlyList<GeneratedInstruction> instructions,
        List<VerificationDiagnostic> diagnostics)
    {
        if (body.ExceptionRegions.Any())
        {
            diagnostics.Add(new VerificationDiagnostic("V-EXCEPTION", "exception handlers are not allowed"));
        }

        HashSet<int>? branchTargets = null;
        foreach (var instruction in instructions)
        {
            var opcode = instruction.Opcode;
            if (Forbidden.Contains(opcode) || !Allowed.Contains(opcode))
            {
                diagnostics.Add(new VerificationDiagnostic("V-OPCODE", $"opcode '{opcode}' is not allowed"));
            }

            VerifyOperand(reader, policy, instruction, diagnostics, ref branchTargets);
        }

        if (branchTargets is null)
        {
            return;
        }

        var instructionOffsets = new HashSet<int>();
        foreach (var instruction in instructions)
        {
            instructionOffsets.Add(instruction.Offset);
        }

        foreach (var target in branchTargets)
        {
            if (!instructionOffsets.Contains(target))
            {
                diagnostics.Add(new VerificationDiagnostic(
                    "V-CONTROL-FLOW",
                    $"branch target offset {target} is not a valid instruction"));
            }
        }
    }

    private static void VerifyOperand(
        MetadataReader reader,
        VerificationPolicy policy,
        GeneratedInstruction instruction,
        List<VerificationDiagnostic> diagnostics,
        ref HashSet<int>? branchTargets)
    {
        var opcode = instruction.Opcode;
        if (opcode is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj)
        {
            if (instruction.OperandHandle is not { } handle)
            {
                return;
            }

            if (opcode == ILOpCode.Call && handle.Kind == HandleKind.MethodDefinition)
            {
                VerifyLocalCall(reader, (MethodDefinitionHandle)handle, diagnostics);
                return;
            }

            // Reuse the member signature decoded once during IL reading
            // (GeneratedInstruction.CalledMember) instead of decoding it again here.
            var signature = instruction.CalledMember ?? MetadataName.MemberSignature(reader, handle).Signature;
            if (!policy.IsMemberAllowed(signature))
            {
                diagnostics.Add(new VerificationDiagnostic("V-MEMBER", $"member '{signature}' is not allowed"));
            }

            return;
        }

        if (opcode == ILOpCode.Newarr)
        {
            if (instruction.OperandHandle is not { } handle)
            {
                return;
            }

            var name = MetadataName.Type(reader, handle);
            if (!StringComparer.Ordinal.Equals(name, VerifierTypeNames.SandboxValueName))
            {
                diagnostics.Add(new VerificationDiagnostic("V-ARRAY", $"array element type '{name}' is not allowed"));
            }

            return;
        }

        if (opcode == ILOpCode.Switch)
        {
            foreach (var target in instruction.SwitchTargets)
            {
                AddBranchTarget(ref branchTargets, target);
            }

            return;
        }

        if (opcode.IsBranch())
        {
            if (instruction.BranchTarget is { } target)
            {
                AddBranchTarget(ref branchTargets, target);
            }
        }
    }

    private static void AddBranchTarget(ref HashSet<int>? branchTargets, int target)
    {
        branchTargets ??= [];
        branchTargets.Add(target);
    }

    private static void VerifyLocalCall(
        MetadataReader reader,
        MethodDefinitionHandle handle,
        List<VerificationDiagnostic> diagnostics)
    {
        var method = reader.GetMethodDefinition(handle);
        if ((method.Attributes & MethodAttributes.Static) == 0)
        {
            diagnostics.Add(new VerificationDiagnostic("V-MEMBER", "local method calls must target static methods"));
        }
    }
}

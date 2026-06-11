namespace SafeIR.Verifier;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

internal static class OpCodeVerifier
{
    private static readonly HashSet<ILOpCode> Allowed = [
        ILOpCode.Nop, ILOpCode.Ldarg_0, ILOpCode.Ldarg_1, ILOpCode.Ldarg_2, ILOpCode.Ldarg_3,
        ILOpCode.Ldarg, ILOpCode.Ldarg_s, ILOpCode.Starg, ILOpCode.Starg_s,
        ILOpCode.Ldloc_0, ILOpCode.Ldloc_1, ILOpCode.Ldloc_2, ILOpCode.Ldloc_3,
        ILOpCode.Ldloc, ILOpCode.Ldloc_s, ILOpCode.Stloc_0, ILOpCode.Stloc_1, ILOpCode.Stloc_2, ILOpCode.Stloc_3,
        ILOpCode.Stloc, ILOpCode.Stloc_s, ILOpCode.Ldnull, ILOpCode.Ldc_i4, ILOpCode.Ldc_i4_s,
        ILOpCode.Ldc_i4_0, ILOpCode.Ldc_i4_1, ILOpCode.Ldc_i4_2, ILOpCode.Ldc_i4_3, ILOpCode.Ldc_i4_4,
        ILOpCode.Ldc_i4_5, ILOpCode.Ldc_i4_6, ILOpCode.Ldc_i4_7, ILOpCode.Ldc_i4_8, ILOpCode.Ldc_i4_m1,
        ILOpCode.Ldc_r8, ILOpCode.Ldstr, ILOpCode.Br, ILOpCode.Br_s, ILOpCode.Brtrue, ILOpCode.Brtrue_s,
        ILOpCode.Brfalse, ILOpCode.Brfalse_s, ILOpCode.Beq, ILOpCode.Beq_s, ILOpCode.Bne_un, ILOpCode.Bne_un_s,
        ILOpCode.Blt, ILOpCode.Blt_s, ILOpCode.Bgt, ILOpCode.Bgt_s, ILOpCode.Ble, ILOpCode.Ble_s,
        ILOpCode.Bge, ILOpCode.Bge_s, ILOpCode.Add, ILOpCode.Sub, ILOpCode.Mul, ILOpCode.Div,
        ILOpCode.Rem, ILOpCode.Neg, ILOpCode.And, ILOpCode.Or, ILOpCode.Xor, ILOpCode.Not,
        ILOpCode.Ceq, ILOpCode.Clt, ILOpCode.Cgt, ILOpCode.Call, ILOpCode.Ret, ILOpCode.Pop, ILOpCode.Dup,
        ILOpCode.Newarr, ILOpCode.Stelem_ref
    ];

    private static readonly HashSet<ILOpCode> Forbidden = [
        ILOpCode.Calli, ILOpCode.Jmp, ILOpCode.Localloc, ILOpCode.Cpblk, ILOpCode.Initblk,
        ILOpCode.Ldftn, ILOpCode.Ldvirtftn, ILOpCode.Ldtoken, ILOpCode.Mkrefany,
        ILOpCode.Refanytype, ILOpCode.Refanyval, ILOpCode.Arglist, ILOpCode.Throw,
        ILOpCode.Rethrow, ILOpCode.Box, ILOpCode.Unbox, ILOpCode.Unbox_any,
        ILOpCode.Castclass, ILOpCode.Isinst, ILOpCode.Ldsfld, ILOpCode.Stsfld
    ];

    public static void VerifyBody(
        MetadataReader reader,
        VerificationPolicy policy,
        MethodBodyBlock body,
        List<VerificationDiagnostic> diagnostics)
    {
        if (body.ExceptionRegions.Any()) {
            diagnostics.Add(new VerificationDiagnostic("V-EXCEPTION", "exception handlers are not allowed"));
        }

        var il = body.GetILReader();
        var instructionOffsets = new HashSet<int>();
        var branchTargets = new HashSet<int>();
        while (il.RemainingBytes > 0) {
            instructionOffsets.Add(il.Offset);
            var opcode = ReadOpCode(ref il);
            if (Forbidden.Contains(opcode) || !Allowed.Contains(opcode)) {
                diagnostics.Add(new VerificationDiagnostic("V-OPCODE", $"opcode '{opcode}' is not allowed"));
            }

            VerifyOperand(reader, policy, opcode, ref il, diagnostics, branchTargets);
        }

        foreach (var target in branchTargets) {
            if (!instructionOffsets.Contains(target)) {
                diagnostics.Add(new VerificationDiagnostic(
                    "V-CONTROL-FLOW",
                    $"branch target offset {target} is not a valid instruction"));
            }
        }
    }

    private static ILOpCode ReadOpCode(ref BlobReader il)
    {
        var first = il.ReadByte();
        if (first != 0xFE) {
            return (ILOpCode)first;
        }

        return (ILOpCode)(0xFE00 | il.ReadByte());
    }

    private static void VerifyOperand(
        MetadataReader reader,
        VerificationPolicy policy,
        ILOpCode opcode,
        ref BlobReader il,
        List<VerificationDiagnostic> diagnostics,
        HashSet<int> branchTargets)
    {
        if (opcode is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj) {
            var handle = MetadataTokens.EntityHandle(il.ReadInt32());
            if (opcode == ILOpCode.Call && handle.Kind == HandleKind.MethodDefinition) {
                VerifyLocalCall(reader, (MethodDefinitionHandle)handle, diagnostics);
                return;
            }

            var member = MetadataName.MemberSignature(reader, handle);
            if (!policy.IsMemberAllowed(member.Signature)) {
                diagnostics.Add(new VerificationDiagnostic("V-MEMBER", $"member '{member.Signature}' is not allowed"));
            }

            return;
        }

        if (opcode == ILOpCode.Newarr) {
            var handle = MetadataTokens.EntityHandle(il.ReadInt32());
            var name = MetadataName.Type(reader, handle);
            if (!StringComparer.Ordinal.Equals(name, "SafeIR.SandboxValue")) {
                diagnostics.Add(new VerificationDiagnostic("V-ARRAY", $"array element type '{name}' is not allowed"));
            }

            return;
        }

        SkipOperand(opcode, ref il, branchTargets);
    }

    private static void VerifyLocalCall(
        MetadataReader reader,
        MethodDefinitionHandle handle,
        List<VerificationDiagnostic> diagnostics)
    {
        var method = reader.GetMethodDefinition(handle);
        if ((method.Attributes & MethodAttributes.Static) == 0) {
            diagnostics.Add(new VerificationDiagnostic("V-MEMBER", "local method calls must target static methods"));
        }
    }

    private static void SkipOperand(ILOpCode opcode, ref BlobReader il, HashSet<int> branchTargets)
    {
        if (opcode == ILOpCode.Switch) {
            var count = il.ReadInt32();
            var deltas = new int[count];
            for (var i = 0; i < count; i++) {
                deltas[i] = il.ReadInt32();
            }

            var nextOffset = il.Offset;
            foreach (var delta in deltas) {
                branchTargets.Add(nextOffset + delta);
            }

            return;
        }

        if (opcode.IsBranch()) {
            var delta = opcode.GetBranchOperandSize() == 1 ? il.ReadSByte() : il.ReadInt32();
            branchTargets.Add(il.Offset + delta);
            return;
        }

        switch (opcode) {
            case ILOpCode.Ldarg or ILOpCode.Starg or ILOpCode.Ldloc or ILOpCode.Stloc:
                _ = il.ReadUInt16();
                break;
            case ILOpCode.Ldarg_s or ILOpCode.Starg_s or ILOpCode.Ldloc_s or ILOpCode.Stloc_s or ILOpCode.Ldc_i4_s:
                _ = il.ReadByte();
                break;
            case ILOpCode.Ldc_i4 or ILOpCode.Ldstr:
                _ = il.ReadInt32();
                break;
            case ILOpCode.Ldc_r8:
                _ = il.ReadDouble();
                break;
            case ILOpCode.Switch:
                var count = il.ReadInt32();
                for (var i = 0; i < count; i++) {
                    _ = il.ReadInt32();
                }

                break;
        }
    }
}

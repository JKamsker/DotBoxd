namespace SafeIR.Verifier;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

internal static class GeneratedIlReader
{
    public static IReadOnlyList<GeneratedInstruction> ReadInstructions(
        MetadataReader reader,
        MethodBodyBlock body,
        List<VerificationDiagnostic> diagnostics)
    {
        var instructions = new List<GeneratedInstruction>();
        var il = body.GetILReader();
        while (il.RemainingBytes > 0)
        {
            if (!TryReadInstruction(reader, ref il, out var instruction, out var diagnostic))
            {
                diagnostics.Add(diagnostic!);
                break;
            }

            instructions.Add(instruction);
        }

        return instructions;
    }

    private static bool TryReadInstruction(
        MetadataReader reader,
        ref BlobReader il,
        out GeneratedInstruction instruction,
        out VerificationDiagnostic? diagnostic)
    {
        var offset = il.Offset;
        instruction = default!;
        diagnostic = null;
        try
        {
            var opcode = ReadOpCode(ref il);
            var operand = ReadOperand(reader, opcode, ref il);
            instruction = new GeneratedInstruction(
                offset,
                opcode,
                il.Offset,
                operand.BranchTarget,
                operand.SwitchTargets,
                operand.CalledMember,
                operand.IsLocalCall,
                operand.Handle,
                operand.Int32Value);
            return true;
        }
        catch (Exception ex) when (IsIlFormatException(ex))
        {
            diagnostic = new VerificationDiagnostic(
                "V-IL-FORMAT",
                $"invalid IL operand at offset {offset}: {ex.Message}");
            return false;
        }
    }

    private static ILOpCode ReadOpCode(ref BlobReader il)
    {
        var first = il.ReadByte();
        return first == 0xFE ? (ILOpCode)(0xFE00 | il.ReadByte()) : (ILOpCode)first;
    }

    private static DecodedOperand ReadOperand(MetadataReader reader, ILOpCode opcode, ref BlobReader il)
    {
        if (opcode is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj)
        {
            var handle = MetadataTokens.EntityHandle(il.ReadInt32());
            var member = MetadataName.MemberSignature(reader, handle);
            return new DecodedOperand(
                handle,
                member.Signature,
                handle.Kind == HandleKind.MethodDefinition);
        }

        if (opcode == ILOpCode.Newarr)
        {
            return new DecodedOperand(ReadHandle(ref il));
        }

        if (opcode == ILOpCode.Switch)
        {
            var count = il.ReadInt32();
            if (count < 0)
            {
                throw new BadImageFormatException("switch table count is negative");
            }

            var deltas = new int[count];
            for (var i = 0; i < count; i++)
            {
                deltas[i] = il.ReadInt32();
            }

            var nextOffset = il.Offset;
            return new DecodedOperand(SwitchTargetsValue: deltas.Select(delta => nextOffset + delta).ToArray());
        }

        if (opcode.IsBranch())
        {
            var delta = opcode.GetBranchOperandSize() == 1 ? il.ReadSByte() : il.ReadInt32();
            return new DecodedOperand(BranchTarget: il.Offset + delta);
        }

        if (TryReadInlineInt32(opcode, ref il, out var value))
        {
            return new DecodedOperand(Int32Value: value);
        }

        SkipOperand(opcode, ref il);
        return DecodedOperand.None;
    }

    private static EntityHandle ReadHandle(ref BlobReader il)
        => MetadataTokens.EntityHandle(il.ReadInt32());

    private static bool TryReadInlineInt32(ILOpCode opcode, ref BlobReader il, out int value)
    {
        switch (opcode)
        {
            case ILOpCode.Ldc_i4_m1:
                value = -1;
                return true;
            case ILOpCode.Ldc_i4_0:
                value = 0;
                return true;
            case ILOpCode.Ldc_i4_1:
                value = 1;
                return true;
            case ILOpCode.Ldc_i4_2:
                value = 2;
                return true;
            case ILOpCode.Ldc_i4_3:
                value = 3;
                return true;
            case ILOpCode.Ldc_i4_4:
                value = 4;
                return true;
            case ILOpCode.Ldc_i4_5:
                value = 5;
                return true;
            case ILOpCode.Ldc_i4_6:
                value = 6;
                return true;
            case ILOpCode.Ldc_i4_7:
                value = 7;
                return true;
            case ILOpCode.Ldc_i4_8:
                value = 8;
                return true;
            case ILOpCode.Ldc_i4_s:
                value = il.ReadSByte();
                return true;
            case ILOpCode.Ldc_i4:
                value = il.ReadInt32();
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static void SkipOperand(ILOpCode opcode, ref BlobReader il)
    {
        switch (opcode)
        {
            case ILOpCode.Ldarg or ILOpCode.Starg or ILOpCode.Ldloc or ILOpCode.Stloc:
                _ = il.ReadUInt16();
                break;
            case ILOpCode.Ldarg_s or ILOpCode.Starg_s or ILOpCode.Ldloc_s or ILOpCode.Stloc_s
                or ILOpCode.Ldc_i4_s or ILOpCode.Unaligned:
                _ = il.ReadByte();
                break;
            case ILOpCode.Ldc_i4 or ILOpCode.Ldstr:
                _ = il.ReadInt32();
                break;
            case ILOpCode.Ldc_i8:
                _ = il.ReadInt64();
                break;
            case ILOpCode.Ldc_r4:
                _ = il.ReadSingle();
                break;
            case ILOpCode.Ldc_r8:
                _ = il.ReadDouble();
                break;
            case ILOpCode.Calli or ILOpCode.Jmp or ILOpCode.Ldftn or ILOpCode.Ldvirtftn
                or ILOpCode.Ldtoken or ILOpCode.Box or ILOpCode.Unbox or ILOpCode.Unbox_any
                or ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Cpobj or ILOpCode.Ldobj
                or ILOpCode.Stobj or ILOpCode.Initobj or ILOpCode.Sizeof or ILOpCode.Constrained
                or ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld or ILOpCode.Ldsfld
                or ILOpCode.Ldsflda or ILOpCode.Stsfld:
                _ = il.ReadInt32();
                break;
        }
    }

    private static bool IsIlFormatException(Exception exception)
        => exception is BadImageFormatException or ArgumentOutOfRangeException or InvalidOperationException;

    private sealed record DecodedOperand(
        EntityHandle? Handle = null,
        string? CalledMember = null,
        bool IsLocalCall = false,
        int? BranchTarget = null,
        IReadOnlyList<int>? SwitchTargetsValue = null,
        int? Int32Value = null)
    {
        public static DecodedOperand None { get; } = new();

        public IReadOnlyList<int> SwitchTargets => SwitchTargetsValue ?? [];
    }
}

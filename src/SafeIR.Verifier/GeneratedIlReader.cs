namespace SafeIR.Verifier;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

internal static class GeneratedIlReader
{
    public static ILOpCode ReadOpCode(ref BlobReader il)
    {
        var first = il.ReadByte();
        return first == 0xFE ? (ILOpCode)(0xFE00 | il.ReadByte()) : (ILOpCode)first;
    }

    public static void SkipOperand(ILOpCode opcode, ref BlobReader il)
    {
        if (opcode == ILOpCode.Switch)
        {
            SkipSwitchOperand(ref il);
            return;
        }

        if (opcode.IsBranch())
        {
            SkipBranchOperand(opcode, ref il);
            return;
        }

        switch (opcode)
        {
            case ILOpCode.Ldarg or ILOpCode.Starg or ILOpCode.Ldloc or ILOpCode.Stloc:
                _ = il.ReadUInt16();
                break;
            case ILOpCode.Ldarg_s or ILOpCode.Starg_s or ILOpCode.Ldloc_s or ILOpCode.Stloc_s or ILOpCode.Ldc_i4_s:
                _ = il.ReadByte();
                break;
            case ILOpCode.Ldc_i4 or ILOpCode.Ldstr or ILOpCode.Newarr:
                _ = il.ReadInt32();
                break;
            case ILOpCode.Ldc_r8:
                _ = il.ReadDouble();
                break;
        }
    }

    private static void SkipSwitchOperand(ref BlobReader il)
    {
        var count = il.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            _ = il.ReadInt32();
        }
    }

    private static void SkipBranchOperand(ILOpCode opcode, ref BlobReader il)
    {
        if (opcode.GetBranchOperandSize() == 1)
        {
            _ = il.ReadSByte();
        }
        else
        {
            _ = il.ReadInt32();
        }
    }
}

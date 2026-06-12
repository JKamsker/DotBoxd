namespace SafeIR.Verifier;

using System.Reflection.Metadata;

internal static class GeneratedStackVerifier
{
    public static void Verify(
        MetadataReader reader,
        MethodDefinition method,
        GeneratedMethodFlow analysis,
        List<VerificationDiagnostic> diagnostics)
    {
        var returnCount = ReturnCount(reader, method);
        var depths = new Dictionary<int, int>();
        var queue = new Queue<int>();
        if (analysis.Instructions.Count == 0)
        {
            return;
        }

        depths[analysis.Instructions[0].Offset] = 0;
        queue.Enqueue(analysis.Instructions[0].Offset);
        while (queue.Count > 0)
        {
            var offset = queue.Dequeue();
            var instruction = analysis.ByOffset[offset];
            var outputDepth = OutputDepth(instruction, depths[offset], returnCount, diagnostics);
            foreach (var successor in GeneratedMethodFlowAnalyzer.Successors(
                         analysis.Instructions,
                         analysis.ByOffset,
                         instruction))
            {
                TrackSuccessorDepth(successor, outputDepth, analysis.ByOffset, depths, queue, diagnostics);
            }
        }
    }

    private static int OutputDepth(
        GeneratedInstruction instruction,
        int inputDepth,
        int returnCount,
        List<VerificationDiagnostic> diagnostics)
    {
        if (instruction.Opcode == ILOpCode.Ret && inputDepth != returnCount)
        {
            diagnostics.Add(new VerificationDiagnostic("V-STACK", "return stack height does not match method signature"));
        }

        var outputDepth = inputDepth + StackDelta(instruction);
        if (outputDepth < 0)
        {
            diagnostics.Add(new VerificationDiagnostic("V-STACK", "method body has an operand stack underflow"));
            return 0;
        }

        return outputDepth;
    }

    private static void TrackSuccessorDepth(
        int successor,
        int outputDepth,
        IReadOnlyDictionary<int, GeneratedInstruction> byOffset,
        Dictionary<int, int> depths,
        Queue<int> queue,
        List<VerificationDiagnostic> diagnostics)
    {
        if (!byOffset.ContainsKey(successor))
        {
            return;
        }

        if (!depths.TryGetValue(successor, out var existing))
        {
            depths[successor] = outputDepth;
            queue.Enqueue(successor);
            return;
        }

        if (existing != outputDepth)
        {
            diagnostics.Add(new VerificationDiagnostic("V-STACK", "branch target has inconsistent stack height"));
        }
    }

    private static int StackDelta(GeneratedInstruction instruction)
        => instruction.Opcode switch
        {
            ILOpCode.Ldarg or ILOpCode.Ldarg_s or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2
                or ILOpCode.Ldarg_3 or ILOpCode.Ldloc or ILOpCode.Ldloc_s or ILOpCode.Ldloc_0
                or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3 or ILOpCode.Ldnull
                or ILOpCode.Ldc_i4 or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1
                or ILOpCode.Ldc_i4_2 or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
                or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8 or ILOpCode.Ldc_i4_m1
                or ILOpCode.Ldc_r8 or ILOpCode.Ldstr => 1,
            ILOpCode.Starg or ILOpCode.Starg_s or ILOpCode.Stloc or ILOpCode.Stloc_s or ILOpCode.Stloc_0
                or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3 or ILOpCode.Pop
                or ILOpCode.Brtrue or ILOpCode.Brtrue_s or ILOpCode.Brfalse or ILOpCode.Brfalse_s
                or ILOpCode.Switch => -1,
            ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div or ILOpCode.Rem
                or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor or ILOpCode.Ceq or ILOpCode.Clt
                or ILOpCode.Cgt => -1,
            ILOpCode.Beq or ILOpCode.Beq_s or ILOpCode.Bne_un or ILOpCode.Bne_un_s
                or ILOpCode.Blt or ILOpCode.Blt_s or ILOpCode.Bgt or ILOpCode.Bgt_s
                or ILOpCode.Ble or ILOpCode.Ble_s or ILOpCode.Bge or ILOpCode.Bge_s => -2,
            ILOpCode.Dup => 1,
            ILOpCode.Stelem_ref => -3,
            ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj => CallDelta(instruction.CalledMember),
            _ => 0
        };

    private static int CallDelta(string? signature)
    {
        if (signature is null)
        {
            return 0;
        }

        return -ParameterCount(signature) + (ReturnsVoid(signature) ? 0 : 1);
    }

    private static int ParameterCount(string signature)
    {
        var start = signature.IndexOf('(');
        var end = signature.LastIndexOf("):", StringComparison.Ordinal);
        if (start < 0 || end <= start + 1)
        {
            return 0;
        }

        return signature[(start + 1)..end].Split(',').Length;
    }

    private static bool ReturnsVoid(string signature)
        => signature.EndsWith(":System.Void", StringComparison.Ordinal);

    private static int ReturnCount(MetadataReader reader, MethodDefinition method)
        => method.DecodeSignature(MethodSignatureNameProvider.Instance, genericContext: null).ReturnType == "System.Void"
            ? 0
            : 1;
}

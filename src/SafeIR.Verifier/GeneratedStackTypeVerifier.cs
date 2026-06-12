namespace SafeIR.Verifier;

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using static SafeIR.Verifier.GeneratedStackTypeOperations;
using static SafeIR.Verifier.VerifierTypeNames;

internal static class GeneratedStackTypeVerifier
{
    public static void Verify(
        MetadataReader reader,
        MethodDefinition method,
        MethodBodyBlock body,
        GeneratedMethodFlow analysis,
        List<VerificationDiagnostic> diagnostics)
    {
        if (analysis.Instructions.Count == 0)
        {
            return;
        }

        var signature = GeneratedMethodSignatureReader.Read(reader, method, body);
        var stacks = new Dictionary<int, IReadOnlyList<string>>();
        var queue = new Queue<int>();
        stacks[analysis.Instructions[0].Offset] = [];
        queue.Enqueue(analysis.Instructions[0].Offset);

        while (queue.Count > 0)
        {
            var offset = queue.Dequeue();
            var instruction = analysis.ByOffset[offset];
            var output = Transfer(instruction, stacks[offset], signature, diagnostics);
            foreach (var successor in GeneratedMethodFlowAnalyzer.Successors(
                         analysis.Instructions,
                         analysis.ByOffset,
                         instruction))
            {
                TrackSuccessor(successor, output, analysis.ByOffset, stacks, queue, diagnostics);
            }
        }
    }

    private static IReadOnlyList<string> Transfer(
        GeneratedInstruction instruction,
        IReadOnlyList<string> input,
        GeneratedMethodSignature signature,
        List<VerificationDiagnostic> diagnostics)
    {
        var stack = input.ToList();
        switch (instruction.Opcode)
        {
            case ILOpCode.Ldarg or ILOpCode.Ldarg_s or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1
                or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3:
                stack.Add(IndexedType("argument", signature.Arguments, instruction, diagnostics));
                break;
            case ILOpCode.Ldloc or ILOpCode.Ldloc_s or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1
                or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3:
                stack.Add(IndexedType("local", signature.Locals, instruction, diagnostics));
                break;
            case ILOpCode.Starg or ILOpCode.Starg_s:
                StoreIndexed("argument", signature.Arguments, instruction, stack, diagnostics);
                break;
            case ILOpCode.Stloc or ILOpCode.Stloc_s or ILOpCode.Stloc_0 or ILOpCode.Stloc_1
                or ILOpCode.Stloc_2 or ILOpCode.Stloc_3:
                StoreIndexed("local", signature.Locals, instruction, stack, diagnostics);
                break;
            case ILOpCode.Ldnull:
                stack.Add(NullType);
                break;
            case ILOpCode.Ldstr:
                stack.Add(StringName);
                break;
            case ILOpCode.Ldc_i4 or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1
                or ILOpCode.Ldc_i4_2 or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
                or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8 or ILOpCode.Ldc_i4_m1:
                stack.Add(Int32Name);
                break;
            case ILOpCode.Ldc_i8:
                stack.Add(Int64Name);
                break;
            case ILOpCode.Ldc_r8:
                stack.Add(DoubleName);
                break;
            case ILOpCode.Pop:
                PopAny(instruction, stack, diagnostics);
                break;
            case ILOpCode.Dup:
                Duplicate(instruction, stack, diagnostics);
                break;
            case ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div or ILOpCode.Rem
                or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor:
                BinaryNumeric(instruction, stack, diagnostics);
                break;
            case ILOpCode.Neg or ILOpCode.Not:
                UnaryNumeric(instruction, stack, diagnostics);
                break;
            case ILOpCode.Ceq or ILOpCode.Clt or ILOpCode.Cgt:
                Compare(instruction, stack, diagnostics);
                break;
            case ILOpCode.Beq or ILOpCode.Beq_s or ILOpCode.Bne_un or ILOpCode.Bne_un_s
                or ILOpCode.Blt or ILOpCode.Blt_s or ILOpCode.Bgt or ILOpCode.Bgt_s
                or ILOpCode.Ble or ILOpCode.Ble_s or ILOpCode.Bge or ILOpCode.Bge_s:
                CompareBranch(instruction, stack, diagnostics);
                break;
            case ILOpCode.Brtrue or ILOpCode.Brtrue_s or ILOpCode.Brfalse or ILOpCode.Brfalse_s:
                BranchCondition(instruction, stack, diagnostics);
                break;
            case ILOpCode.Switch:
                PopExpected(instruction, stack, Int32Name, diagnostics);
                break;
            case ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj:
                Call(instruction, stack, diagnostics);
                break;
            case ILOpCode.Stelem_ref:
                StoreElement(instruction, stack, diagnostics);
                break;
            case ILOpCode.Ret:
                Return(instruction, stack, signature.ReturnType, diagnostics);
                break;
        }

        return stack;
    }

    private static string IndexedType(
        string kind,
        IReadOnlyList<string> types,
        GeneratedInstruction instruction,
        List<VerificationDiagnostic> diagnostics)
    {
        if (instruction.OperandIndex is not { } index || index < 0 || index >= types.Count)
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-OPERAND",
                $"{kind} index {instruction.OperandIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<missing>"} is outside the method {kind} range"));
            return UnknownType;
        }

        return types[index];
    }

    private static void StoreIndexed(
        string kind,
        IReadOnlyList<string> types,
        GeneratedInstruction instruction,
        List<string> stack,
        List<VerificationDiagnostic> diagnostics)
    {
        var expected = IndexedType(kind, types, instruction, diagnostics);
        PopExpected(instruction, stack, expected, diagnostics);
    }

    private static void TrackSuccessor(
        int successor,
        IReadOnlyList<string> output,
        IReadOnlyDictionary<int, GeneratedInstruction> byOffset,
        Dictionary<int, IReadOnlyList<string>> stacks,
        Queue<int> queue,
        List<VerificationDiagnostic> diagnostics)
    {
        if (!byOffset.ContainsKey(successor))
        {
            return;
        }

        if (!stacks.TryGetValue(successor, out var existing))
        {
            stacks[successor] = output.ToArray();
            queue.Enqueue(successor);
            return;
        }

        if (!existing.SequenceEqual(output, StringComparer.Ordinal))
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-STACK-TYPE",
                "branch target has inconsistent stack types"));
        }
    }
}

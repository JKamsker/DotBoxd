using System.Reflection.Metadata;
using DotBoxD.Kernels.Verifier.Generated.Methods;

namespace DotBoxD.Kernels.Verifier.Generated;

using static GeneratedStackTypeOperations;
using static Verifier.VerifierTypeNames;

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
        var stacks = new Dictionary<int, StackTypeSnapshot>();
        var queue = new Queue<int>();
        stacks[analysis.Instructions[0].Offset] = StackTypeSnapshot.Empty;
        queue.Enqueue(analysis.Instructions[0].Offset);

        // Reused mutable working buffer for the transfer of each instruction.
        // The stored per-offset snapshots stay immutable, so refilling this
        // buffer from the incoming snapshot avoids cloning the whole stack on
        // every reachable instruction.
        var stack = new List<string>();
        var callSignatures = new ParsedCallSignatureCache();

        while (queue.Count > 0)
        {
            var offset = queue.Dequeue();
            var instruction = analysis.ByOffset[offset];
            Transfer(instruction, stacks[offset], stack, signature, callSignatures, diagnostics);
            foreach (var successor in analysis.SuccessorsByOffset[instruction.Offset])
            {
                TrackSuccessor(successor, stack, analysis.ByOffset, stacks, queue, diagnostics);
            }
        }
    }

    private static void Transfer(
        GeneratedInstruction instruction,
        StackTypeSnapshot input,
        List<string> stack,
        GeneratedMethodSignature signature,
        ParsedCallSignatureCache callSignatures,
        List<VerificationDiagnostic> diagnostics)
    {
        input.CopyTo(stack);
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
                Call(instruction, stack, callSignatures, diagnostics);
                break;
            case ILOpCode.Stelem_ref:
                StoreElement(instruction, stack, diagnostics);
                break;
            case ILOpCode.Ret:
                Return(instruction, stack, signature.ReturnType, diagnostics);
                break;
        }
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
        List<string> output,
        IReadOnlyDictionary<int, GeneratedInstruction> byOffset,
        Dictionary<int, StackTypeSnapshot> stacks,
        Queue<int> queue,
        List<VerificationDiagnostic> diagnostics)
    {
        if (!byOffset.ContainsKey(successor))
        {
            return;
        }

        if (!stacks.TryGetValue(successor, out var existing))
        {
            stacks[successor] = StackTypeSnapshot.From(output);
            queue.Enqueue(successor);
            return;
        }

        if (!existing.Matches(output))
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-STACK-TYPE",
                "branch target has inconsistent stack types"));
        }
    }

    private readonly struct StackTypeSnapshot
    {
        private readonly string? _first;
        private readonly string? _second;
        private readonly string[]? _items;

        private StackTypeSnapshot(int count, string? first, string? second, string[]? items)
        {
            Count = count;
            _first = first;
            _second = second;
            _items = items;
        }

        public static StackTypeSnapshot Empty { get; } = new(0, null, null, null);

        public int Count { get; }

        public static StackTypeSnapshot From(IReadOnlyList<string> stack)
            => stack.Count switch {
                0 => Empty,
                1 => new StackTypeSnapshot(1, stack[0], null, null),
                2 => new StackTypeSnapshot(2, stack[0], stack[1], null),
                _ => new StackTypeSnapshot(stack.Count, null, null, Copy(stack))
            };

        public void CopyTo(List<string> stack)
        {
            stack.Clear();
            switch (Count)
            {
                case 0:
                    return;
                case 1:
                    stack.Add(_first!);
                    return;
                case 2:
                    stack.Add(_first!);
                    stack.Add(_second!);
                    return;
                default:
                    stack.AddRange(_items!);
                    return;
            }
        }

        public bool Matches(IReadOnlyList<string> stack)
        {
            if (stack.Count != Count)
            {
                return false;
            }

            return Count switch {
                0 => true,
                1 => string.Equals(_first, stack[0], StringComparison.Ordinal),
                2 => string.Equals(_first, stack[0], StringComparison.Ordinal) &&
                     string.Equals(_second, stack[1], StringComparison.Ordinal),
                _ => MatchesItems(stack)
            };
        }

        private bool MatchesItems(IReadOnlyList<string> stack)
        {
            var items = _items!;
            for (var i = 0; i < items.Length; i++)
            {
                if (!string.Equals(items[i], stack[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string[] Copy(IReadOnlyList<string> stack)
        {
            var items = new string[stack.Count];
            for (var i = 0; i < items.Length; i++)
            {
                items[i] = stack[i];
            }

            return items;
        }
    }
}

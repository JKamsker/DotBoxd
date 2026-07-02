using DotBoxD.Kernels.Verifier.Generated.Methods;

namespace DotBoxD.Kernels.Verifier.Generated;

using static Verifier.VerifierTypeNames;

internal static class GeneratedStackTypeOperations
{
    internal const string UnknownType = "?";
    internal const string NullType = "null";

    public static void Call(
        GeneratedInstruction instruction,
        List<string> stack,
        ParsedCallSignatureCache callSignatures,
        List<VerificationDiagnostic> diagnostics)
    {
        var signature = callSignatures.Get(instruction.CalledMember);
        for (var i = signature.Parameters.Count - 1; i >= 0; i--)
        {
            PopExpected(instruction, stack, signature.Parameters[i], diagnostics);
        }

        if (signature.ReturnType != VoidName)
        {
            stack.Add(signature.ReturnType);
        }
    }

    public static void Return(
        GeneratedInstruction instruction,
        List<string> stack,
        string returnType,
        List<VerificationDiagnostic> diagnostics)
    {
        if (returnType == VoidName)
        {
            if (stack.Count != 0)
            {
                diagnostics.Add(new VerificationDiagnostic("V-STACK-TYPE", "void return must leave the stack empty"));
            }

            return;
        }

        if (stack.Count == 0)
        {
            diagnostics.Add(new VerificationDiagnostic("V-STACK", "method body has an operand stack underflow"));
            return;
        }

        if (stack.Count != 1)
        {
            diagnostics.Add(new VerificationDiagnostic("V-STACK-TYPE", "return must leave exactly one value on the stack"));
        }

        var actual = stack[^1];
        if (!IsAssignable(actual, returnType))
        {
            AddTypeDiagnostic(instruction, returnType, actual, diagnostics);
        }
    }

    public static void BinaryNumeric(
        GeneratedInstruction instruction,
        List<string> stack,
        List<VerificationDiagnostic> diagnostics)
    {
        var right = PopAny(instruction, stack, diagnostics);
        var left = PopAny(instruction, stack, diagnostics);
        var result = CommonNumericType(left, right);
        if (result is null)
        {
            AddTypeDiagnostic(instruction, "matching numeric operands", $"{left},{right}", diagnostics);
            stack.Add(UnknownType);
            return;
        }

        stack.Add(result);
    }

    public static void UnaryNumeric(
        GeneratedInstruction instruction,
        List<string> stack,
        List<VerificationDiagnostic> diagnostics)
    {
        var value = PopAny(instruction, stack, diagnostics);
        if (!IsNumeric(value))
        {
            AddTypeDiagnostic(instruction, "numeric operand", value, diagnostics);
            stack.Add(UnknownType);
            return;
        }

        stack.Add(value);
    }

    public static void ConvertToInt64(
        GeneratedInstruction instruction,
        List<string> stack,
        List<VerificationDiagnostic> diagnostics)
    {
        var value = PopAny(instruction, stack, diagnostics);
        if (value is not (Int32Name or UnknownType))
        {
            AddTypeDiagnostic(instruction, Int32Name, value, diagnostics);
            stack.Add(UnknownType);
            return;
        }

        stack.Add(Int64Name);
    }

    public static void ConvertToDouble(
        GeneratedInstruction instruction,
        List<string> stack,
        List<VerificationDiagnostic> diagnostics)
    {
        var value = PopAny(instruction, stack, diagnostics);
        if (value is not (Int32Name or Int64Name or UnknownType))
        {
            AddTypeDiagnostic(instruction, "System.Int32 or System.Int64", value, diagnostics);
            stack.Add(UnknownType);
            return;
        }

        stack.Add(DoubleName);
    }

    public static void Compare(
        GeneratedInstruction instruction,
        List<string> stack,
        List<VerificationDiagnostic> diagnostics)
    {
        var right = PopAny(instruction, stack, diagnostics);
        var left = PopAny(instruction, stack, diagnostics);
        if (CommonComparableType(left, right) is null)
        {
            AddTypeDiagnostic(instruction, "matching comparable operands", $"{left},{right}", diagnostics);
        }

        stack.Add(Int32Name);
    }

    public static void CompareBranch(
        GeneratedInstruction instruction,
        List<string> stack,
        List<VerificationDiagnostic> diagnostics)
    {
        var right = PopAny(instruction, stack, diagnostics);
        var left = PopAny(instruction, stack, diagnostics);
        if (CommonComparableType(left, right) is null)
        {
            AddTypeDiagnostic(instruction, "matching comparable operands", $"{left},{right}", diagnostics);
        }
    }

    public static void BranchCondition(
        GeneratedInstruction instruction,
        List<string> stack,
        List<VerificationDiagnostic> diagnostics)
    {
        var actual = PopAny(instruction, stack, diagnostics);
        if (actual is UnknownType or BooleanName or Int32Name or Int64Name or NullType || IsReferenceType(actual))
        {
            return;
        }

        AddTypeDiagnostic(instruction, "branchable condition", actual, diagnostics);
    }

    public static void StoreElement(
        GeneratedInstruction instruction,
        List<string> stack,
        List<VerificationDiagnostic> diagnostics)
    {
        var value = PopAny(instruction, stack, diagnostics);
        PopExpected(instruction, stack, Int32Name, diagnostics);
        var array = PopAny(instruction, stack, diagnostics);
        if (array == UnknownType)
        {
            return;
        }

        if (!array.EndsWith("[]", StringComparison.Ordinal))
        {
            AddTypeDiagnostic(instruction, "array", array, diagnostics);
            return;
        }

        var elementType = array[..^2];
        if (!IsAssignable(value, elementType))
        {
            AddTypeDiagnostic(instruction, elementType, value, diagnostics);
        }
    }

    public static void Duplicate(
        GeneratedInstruction instruction,
        List<string> stack,
        List<VerificationDiagnostic> diagnostics)
    {
        if (stack.Count == 0)
        {
            diagnostics.Add(new VerificationDiagnostic("V-STACK", "method body has an operand stack underflow"));
            return;
        }

        stack.Add(stack[^1]);
    }

    public static string PopAny(
        GeneratedInstruction instruction,
        List<string> stack,
        List<VerificationDiagnostic> diagnostics)
    {
        if (stack.Count == 0)
        {
            diagnostics.Add(new VerificationDiagnostic("V-STACK", "method body has an operand stack underflow"));
            return UnknownType;
        }

        var actual = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return actual;
    }

    public static void PopExpected(
        GeneratedInstruction instruction,
        List<string> stack,
        string expected,
        List<VerificationDiagnostic> diagnostics)
    {
        var actual = PopAny(instruction, stack, diagnostics);
        if (!IsAssignable(actual, expected))
        {
            AddTypeDiagnostic(instruction, expected, actual, diagnostics);
        }
    }

    private static string? CommonNumericType(string left, string right)
        => left == right && IsNumeric(left) ? left : null;

    private static string? CommonComparableType(string left, string right)
    {
        if (left == right && (IsNumeric(left) || left == BooleanName || IsReferenceType(left)))
        {
            return left;
        }

        if ((left == NullType && IsReferenceType(right)) || (right == NullType && IsReferenceType(left)))
        {
            return NullType;
        }

        return null;
    }

    private static bool IsAssignable(string actual, string expected)
    {
        if (actual == UnknownType || expected == UnknownType || actual == expected)
        {
            return true;
        }

        if (actual == NullType && IsReferenceType(expected))
        {
            return expected != SandboxTypeName;
        }

        if (actual == Int32Name && expected == BooleanName)
        {
            return true;
        }

        return expected == ObjectName && IsReferenceType(actual);
    }

    private static bool IsNumeric(string type)
        => type is Int32Name or Int64Name or DoubleName or UnknownType;

    private static bool IsReferenceType(string type)
        => type is not UnknownType and not NullType and not VoidName and not BooleanName
            and not Int32Name and not Int64Name and not DoubleName;

    private static void AddTypeDiagnostic(
        GeneratedInstruction instruction,
        string expected,
        string actual,
        List<VerificationDiagnostic> diagnostics)
        => diagnostics.Add(new VerificationDiagnostic(
            "V-STACK-TYPE",
            $"opcode '{instruction.Opcode}' expected {expected} but found {actual}"));

}

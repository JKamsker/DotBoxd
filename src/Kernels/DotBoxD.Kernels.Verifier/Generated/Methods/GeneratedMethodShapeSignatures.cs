namespace DotBoxD.Kernels.Verifier.Generated.Methods;

using static Verifier.VerifierTypeNames;

internal static class GeneratedMethodShapeSignatures
{
    internal static readonly string ValidateInput =
        $"{CompiledRuntimeName}.ValidateEntrypointInput({SandboxValueName},{Int32Name}):{VoidName}";
    internal static readonly string EnterCall = $"{CompiledRuntimeName}.EnterCall({SandboxContextName}):{VoidName}";
    internal static readonly string ExitCall = $"{CompiledRuntimeName}.ExitCall({SandboxContextName}):{VoidName}";
    internal static readonly string ChargeFuelSignature =
        $"{CompiledRuntimeName}.ChargeFuel({SandboxContextName},{Int32Name}):{VoidName}";
    internal static readonly string ChargeLoopIterationSignature =
        $"{CompiledRuntimeName}.ChargeLoopIteration({SandboxContextName},{Int32Name}):{VoidName}";
    internal static readonly string ChargeSandboxValueSignature =
        $"{CompiledRuntimeName}.ChargeSandboxValue({SandboxContextName},{SandboxValueName}):{VoidName}";
    internal static readonly string ChargeSandboxValuesSignature =
        $"{CompiledRuntimeName}.ChargeSandboxValues({SandboxContextName},{SandboxValueName},{Int32Name}):{VoidName}";
    // Closed-form linear accumulation (exp/closed-form-accumulation): charges a bulk of loop-iteration fuel
    // internally, so it counts as a fuel meter for the instruction-density / sparsity rule.
    internal static readonly string AccumulateLinearI32Signature =
        $"{CompiledRuntimeName}.AccumulateLinearI32({SandboxContextName},{Int32Name},{Int32Name},{Int32Name},{Int32Name}):{Int32Name}";
    internal static readonly IReadOnlySet<string> ExecuteAllowedCalls = new HashSet<string>(StringComparer.Ordinal) {
        ValidateInput,
        $"{CompiledRuntimeName}.GetInputArgument({SandboxValueName},{Int32Name},{Int32Name},{SandboxTypeName}):{SandboxValueName}",
        $"{CompiledRuntimeName}.TypeScalar({StringName}):{SandboxTypeName}",
        $"{CompiledRuntimeName}.TypeList({SandboxTypeName}):{SandboxTypeName}",
        $"{CompiledRuntimeName}.TypeMap({SandboxTypeName},{SandboxTypeName}):{SandboxTypeName}",
        $"{CompiledRuntimeName}.TypeRecord({SandboxTypeArrayName}):{SandboxTypeName}",
        $"{CompiledRuntimeName}.CreateMeteredTypeArray({SandboxContextName},{Int32Name}):{SandboxTypeArrayName}"
    };

    private static readonly string RequireValueType =
        $"{CompiledRuntimeName}.RequireValueType({SandboxValueName},{SandboxTypeName}):{SandboxValueName}";

    internal static GeneratedMeterState StateFor(GeneratedInstruction instruction)
    {
        var state = instruction.CalledMember switch
        {
            var member when member == ValidateInput => GeneratedMeterState.ValidateInput,
            var member when member == EnterCall => GeneratedMeterState.EnterCall,
            var member when member == ExitCall => GeneratedMeterState.ExitCall,
            var member when member == ChargeFuelSignature => GeneratedMeterState.ChargeFuel,
            var member when member == ChargeLoopIterationSignature => GeneratedMeterState.ChargeFuel,
            var member when member == AccumulateLinearI32Signature => GeneratedMeterState.ChargeFuel,
            _ => GeneratedMeterState.None
        };
        return instruction.IsLocalCall && IsGeneratedFunctionCall(instruction.CalledMember)
            ? state | GeneratedMeterState.LocalFunctionCall
            : state;
    }

    internal static bool IsGeneratedFunctionCall(string? calledMember)
        => calledMember is not null && calledMember.StartsWith("Fn_", StringComparison.Ordinal);

    internal static bool IsFuelMeter(string? calledMember)
        => calledMember == ChargeFuelSignature
           || calledMember == ChargeLoopIterationSignature
           || calledMember == AccumulateLinearI32Signature;

    internal static bool IsRuntimeWorkCall(string? calledMember)
        => calledMember is not null &&
           calledMember.StartsWith(CompiledRuntimeName + ".", StringComparison.Ordinal) &&
           calledMember != EnterCall &&
           calledMember != ExitCall &&
           calledMember != ChargeFuelSignature &&
           calledMember != ChargeLoopIterationSignature &&
           calledMember != RequireValueType;

    internal static bool IsMeterDensityWorkCall(string? calledMember)
    {
        if (!IsRuntimeWorkCall(calledMember) || calledMember is null)
        {
            return false;
        }

        return !calledMember.StartsWith(
                   CompiledRuntimeName + ".CallBinding(" + SandboxContextName,
                   StringComparison.Ordinal) &&
               !calledMember.StartsWith(
                   CompiledRuntimeName + ".CallBinding1(" + SandboxContextName,
                   StringComparison.Ordinal) &&
               !calledMember.StartsWith(
                   CompiledRuntimeName + ".CallBinding2(" + SandboxContextName,
                   StringComparison.Ordinal) &&
               !calledMember.StartsWith(
                   CompiledRuntimeName + ".CreateValueArray(" + SandboxContextName,
                   StringComparison.Ordinal) &&
               !calledMember.StartsWith(
                   CompiledRuntimeName + ".ChargeValueArray(" + SandboxContextName,
                   StringComparison.Ordinal) &&
               !IsScalarConversionCall(calledMember) &&
               !calledMember.Contains("(" + SandboxContextName + ",", StringComparison.Ordinal);
    }

    // O(1) scalar box/unbox conversions the compiler inserts at the boundaries of its unboxed fast path
    // (raw int/bool <-> SandboxValue). They are bounded, allocation-light, and cannot do unbounded work,
    // so — like literal construction — they do not require per-call meter density. The instruction-density
    // (sparsity) rule still bounds any run of them, and they remain ordinary runtime calls that must follow
    // the entered-call/charged-fuel prologue.
    private static bool IsScalarConversionCall(string calledMember)
        => calledMember.StartsWith(CompiledRuntimeName + ".I32(", StringComparison.Ordinal) ||
           calledMember.StartsWith(CompiledRuntimeName + ".I64(", StringComparison.Ordinal) ||
           calledMember.StartsWith(CompiledRuntimeName + ".F64(", StringComparison.Ordinal) ||
           calledMember.StartsWith(CompiledRuntimeName + ".Bool(", StringComparison.Ordinal) ||
           calledMember.StartsWith(CompiledRuntimeName + ".AsI32(", StringComparison.Ordinal) ||
           calledMember.StartsWith(CompiledRuntimeName + ".AsI64(", StringComparison.Ordinal) ||
           calledMember.StartsWith(CompiledRuntimeName + ".AsF64(", StringComparison.Ordinal) ||
           calledMember.StartsWith(CompiledRuntimeName + ".AsBool(", StringComparison.Ordinal);

    internal static bool IsUnchargedLiteralCall(string? calledMember)
        => calledMember is not null &&
           (calledMember.StartsWith(CompiledRuntimeName + ".StringLiteralValue(", StringComparison.Ordinal) ||
            calledMember.StartsWith(CompiledRuntimeName + ".OpaqueIdLiteralValue(", StringComparison.Ordinal) ||
            calledMember.StartsWith(CompiledRuntimeName + ".GuidLiteralValue(", StringComparison.Ordinal) ||
            calledMember.StartsWith(CompiledRuntimeName + ".PathLiteralValue(", StringComparison.Ordinal) ||
            calledMember.StartsWith(CompiledRuntimeName + ".UriLiteralValue(", StringComparison.Ordinal) ||
            calledMember.StartsWith(CompiledRuntimeName + ".ListLiteralValue(", StringComparison.Ordinal) ||
            calledMember.StartsWith(CompiledRuntimeName + ".MapLiteralValue(", StringComparison.Ordinal));
}

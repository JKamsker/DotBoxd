namespace SafeIR.Verifier;

using static SafeIR.Verifier.VerifierTypeNames;

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
    internal static readonly IReadOnlySet<string> ExecuteAllowedCalls = new HashSet<string>(StringComparer.Ordinal) {
        ValidateInput,
        $"{CompiledRuntimeName}.GetInputArgument({SandboxValueName},{Int32Name},{Int32Name},{SandboxTypeName}):{SandboxValueName}",
        $"{CompiledRuntimeName}.TypeScalar({StringName}):{SandboxTypeName}",
        $"{CompiledRuntimeName}.TypeList({SandboxTypeName}):{SandboxTypeName}",
        $"{CompiledRuntimeName}.TypeMap({SandboxTypeName},{SandboxTypeName}):{SandboxTypeName}"
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
            _ => GeneratedMeterState.None
        };
        return instruction.IsLocalCall && IsGeneratedFunctionCall(instruction.CalledMember)
            ? state | GeneratedMeterState.LocalFunctionCall
            : state;
    }

    internal static bool IsGeneratedFunctionCall(string? calledMember)
        => calledMember is not null && calledMember.StartsWith("Fn_", StringComparison.Ordinal);

    internal static bool IsFuelMeter(string? calledMember)
        => calledMember == ChargeFuelSignature || calledMember == ChargeLoopIterationSignature;

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
                   CompiledRuntimeName + ".CreateValueArray(" + SandboxContextName,
                   StringComparison.Ordinal) &&
               !IsLiteralConstructionCall(calledMember) &&
               !calledMember.Contains("(" + SandboxContextName + ",", StringComparison.Ordinal);
    }

    private static bool IsLiteralConstructionCall(string calledMember)
        => calledMember.StartsWith(CompiledRuntimeName + ".CreateLiteralValueArray(", StringComparison.Ordinal) ||
           calledMember.StartsWith(CompiledRuntimeName + ".StringLiteralValue(", StringComparison.Ordinal) ||
           calledMember.StartsWith(CompiledRuntimeName + ".OpaqueIdLiteralValue(", StringComparison.Ordinal) ||
           calledMember.StartsWith(CompiledRuntimeName + ".PathLiteralValue(", StringComparison.Ordinal) ||
           calledMember.StartsWith(CompiledRuntimeName + ".UriLiteralValue(", StringComparison.Ordinal) ||
           calledMember.StartsWith(CompiledRuntimeName + ".ListLiteralValue(", StringComparison.Ordinal) ||
           calledMember.StartsWith(CompiledRuntimeName + ".MapLiteralValue(", StringComparison.Ordinal);
}

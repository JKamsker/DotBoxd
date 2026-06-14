namespace SafeIR.Interpreter.Internal;

using SafeIR;
using SafeIR.Runtime;

internal static class UnaryPureIntrinsicDispatcher
{
    public static bool TryEvaluate(
        CallExpression call,
        ExpressionEvaluator evaluator,
        InterpreterFrame frame,
        SandboxContext context,
        SandboxExecutionOptions options,
        string moduleHash,
        string functionId,
        out ValueTask<SandboxValue> result)
    {
        if (!TryGetMethod(call.Name, out var method) ||
            call.Arguments.Count != 1 ||
            !context.Bindings.TryGet(call.Name, out var binding) ||
            !CanUseDirectIntrinsic(binding, method))
        {
            result = default;
            return false;
        }

        var operand = evaluator.EvaluateAsync(call.Arguments[0], frame);
        result = operand.IsCompletedSuccessfully
            ? new ValueTask<SandboxValue>(InvokeAfterCharge(
                call.Name, method, operand.Result, context, options, moduleHash, functionId))
            : AwaitOperand(call.Name, method, operand, context, options, moduleHash, functionId);
        return true;
    }

    public static bool IsCandidate(string id)
        => id is "math.abs" or "math.sqrt" or "math.floor" or "math.ceil" or "math.round" or "string.length";

    private static async ValueTask<SandboxValue> AwaitOperand(
        string id,
        string method,
        ValueTask<SandboxValue> operand,
        SandboxContext context,
        SandboxExecutionOptions options,
        string moduleHash,
        string functionId)
        => InvokeAfterCharge(
            id,
            method,
            await operand.ConfigureAwait(false),
            context,
            options,
            moduleHash,
            functionId);

    private static SandboxValue InvokeAfterCharge(
        string id,
        string method,
        SandboxValue operand,
        SandboxContext context,
        SandboxExecutionOptions options,
        string moduleHash,
        string functionId)
    {
        var descriptor = context.Bindings.GetDescriptor(id);
        InterpreterTrace.WriteBindingCall(context, options, moduleHash, functionId, descriptor);
        context.ChargeBindingCall(descriptor);
        return method switch {
            nameof(CompiledRuntime.AbsI32) => CompiledRuntime.AbsI32(operand),
            nameof(CompiledRuntime.StringLength) => CompiledRuntime.StringLength(operand),
            nameof(CompiledRuntime.SqrtF64) => CompiledRuntime.SqrtF64(operand),
            nameof(CompiledRuntime.FloorF64) => CompiledRuntime.FloorF64(operand),
            nameof(CompiledRuntime.CeilF64) => CompiledRuntime.CeilF64(operand),
            nameof(CompiledRuntime.RoundF64) => CompiledRuntime.RoundF64(operand),
            _ => throw new InvalidOperationException("unsupported unary math intrinsic")
        };
    }

    private static bool CanUseDirectIntrinsic(BindingSignature binding, string method)
    {
        var (parameterType, returnType) = Shape(method);
        return binding.Compiled is { Kind: "RuntimeStub" } &&
               binding.Compiled.Type == typeof(CompiledRuntime).FullName &&
               binding.Compiled.Method == method &&
               binding.Parameters.Count == 1 &&
               binding.Parameters[0].Equals(parameterType) &&
               binding.ReturnType.Equals(returnType) &&
               binding.RequiredCapability is null &&
               binding.Safety == BindingSafety.PureIntrinsic &&
               binding.AuditLevel == AuditLevel.None &&
               (binding.Effects & ~(SandboxEffect.Cpu | SandboxEffect.Alloc)) == SandboxEffect.None;
    }

    private static bool TryGetMethod(string id, out string method)
    {
        method = id switch {
            "math.abs" => nameof(CompiledRuntime.AbsI32),
            "string.length" => nameof(CompiledRuntime.StringLength),
            "math.sqrt" => nameof(CompiledRuntime.SqrtF64),
            "math.floor" => nameof(CompiledRuntime.FloorF64),
            "math.ceil" => nameof(CompiledRuntime.CeilF64),
            "math.round" => nameof(CompiledRuntime.RoundF64),
            _ => ""
        };
        return method.Length > 0;
    }

    private static (SandboxType Parameter, SandboxType Return) Shape(string method)
        => method switch {
            nameof(CompiledRuntime.AbsI32) => (SandboxType.I32, SandboxType.I32),
            nameof(CompiledRuntime.StringLength) => (SandboxType.String, SandboxType.I32),
            _ => (SandboxType.F64, SandboxType.F64)
        };
}

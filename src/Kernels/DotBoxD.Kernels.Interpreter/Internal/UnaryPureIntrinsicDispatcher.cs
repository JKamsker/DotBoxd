using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

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
            !context.Bindings.TryGetDescriptor(call.Name, out var descriptor) ||
            !CanUseDirectIntrinsic(descriptor, method))
        {
            result = default;
            return false;
        }

        var operand = evaluator.EvaluateAsync(call.Arguments[0], frame);
        result = operand.IsCompletedSuccessfully
            ? new ValueTask<SandboxValue>(InvokeAfterCharge(
                descriptor, method, operand.Result, context, options, moduleHash, functionId))
            : AwaitOperand(descriptor, method, operand, context, options, moduleHash, functionId);
        return true;
    }

    public static bool IsCandidate(string id)
        => id is "math.abs" or "math.sqrt" or "math.floor" or "math.ceil" or "math.round"
            or "int32.toStringInvariant" or "string.length";

    private static async ValueTask<SandboxValue> AwaitOperand(
        BindingDescriptor descriptor,
        string method,
        ValueTask<SandboxValue> operand,
        SandboxContext context,
        SandboxExecutionOptions options,
        string moduleHash,
        string functionId)
        => InvokeAfterCharge(
            descriptor,
            method,
            await operand.ConfigureAwait(false),
            context,
            options,
            moduleHash,
            functionId);

    private static SandboxValue InvokeAfterCharge(
        BindingDescriptor descriptor,
        string method,
        SandboxValue operand,
        SandboxContext context,
        SandboxExecutionOptions options,
        string moduleHash,
        string functionId)
    {
        InterpreterTrace.WriteBindingCall(context, options, moduleHash, functionId, descriptor);
        context.ChargeBindingCall(descriptor);
        return method switch
        {
            nameof(Runtime.CompiledRuntime.AbsI32) => Runtime.CompiledRuntime.AbsI32(operand),
            nameof(Runtime.CompiledRuntime.Int32ToStringInvariant) =>
                Runtime.CompiledRuntime.Int32ToStringInvariant(context, operand),
            nameof(Runtime.CompiledRuntime.StringLength) => Runtime.CompiledRuntime.StringLength(operand),
            nameof(Runtime.CompiledRuntime.SqrtF64) => Runtime.CompiledRuntime.SqrtF64(operand),
            nameof(Runtime.CompiledRuntime.FloorF64) => Runtime.CompiledRuntime.FloorF64(operand),
            nameof(Runtime.CompiledRuntime.CeilF64) => Runtime.CompiledRuntime.CeilF64(operand),
            nameof(Runtime.CompiledRuntime.RoundF64) => Runtime.CompiledRuntime.RoundF64(operand),
            _ => throw new InvalidOperationException("unsupported unary math intrinsic")
        };
    }

    private static bool CanUseDirectIntrinsic(BindingDescriptor binding, string method)
    {
        var (parameterType, returnType) = Shape(method);
        return binding.Compiled is { Kind: "RuntimeStub" } &&
               binding.Compiled.Type == typeof(Runtime.CompiledRuntime).FullName &&
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
        method = id switch
        {
            "math.abs" => nameof(Runtime.CompiledRuntime.AbsI32),
            "int32.toStringInvariant" => nameof(Runtime.CompiledRuntime.Int32ToStringInvariant),
            "string.length" => nameof(Runtime.CompiledRuntime.StringLength),
            "math.sqrt" => nameof(Runtime.CompiledRuntime.SqrtF64),
            "math.floor" => nameof(Runtime.CompiledRuntime.FloorF64),
            "math.ceil" => nameof(Runtime.CompiledRuntime.CeilF64),
            "math.round" => nameof(Runtime.CompiledRuntime.RoundF64),
            _ => ""
        };
        return method.Length > 0;
    }

    private static (SandboxType Parameter, SandboxType Return) Shape(string method)
        => method switch
        {
            nameof(Runtime.CompiledRuntime.AbsI32) => (SandboxType.I32, SandboxType.I32),
            nameof(Runtime.CompiledRuntime.Int32ToStringInvariant) => (SandboxType.I32, SandboxType.String),
            nameof(Runtime.CompiledRuntime.StringLength) => (SandboxType.String, SandboxType.I32),
            _ => (SandboxType.F64, SandboxType.F64)
        };
}

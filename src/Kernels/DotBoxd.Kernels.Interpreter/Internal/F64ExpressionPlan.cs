namespace DotBoxd.Kernels.Interpreter.Internal;

using DotBoxd.Kernels;
using DotBoxd.Kernels.Runtime;

internal sealed class F64ExpressionPlan
{
    private readonly ExpressionKind _kind;
    private readonly int _slot;
    private readonly double _literal;
    private readonly F64ExpressionPlan? _operand;

    private F64ExpressionPlan(
        ExpressionKind kind,
        int slot = 0,
        double literal = 0,
        F64ExpressionPlan? operand = null,
        bool preservesNonNegative = false)
    {
        _kind = kind;
        _slot = slot;
        _literal = literal;
        _operand = operand;
        FuelCost = 1 + (operand?.FuelCost ?? 0);
        BindingCallCount = kind is ExpressionKind.Literal or ExpressionKind.RawVariable or ExpressionKind.BoxedVariable
            ? 0
            : 1 + (operand?.BindingCallCount ?? 0);
        PreservesNonNegative = preservesNonNegative;
    }

    public int FuelCost { get; }
    public int BindingCallCount { get; }
    public bool PreservesNonNegative { get; }

    public double Evaluate(InterpreterFrame frame)
        => _kind switch {
            ExpressionKind.Literal => _literal,
            ExpressionKind.RawVariable => frame.ReadRawDoubleSlot(_slot),
            ExpressionKind.BoxedVariable => frame.ReadDoubleSlot(_slot),
            ExpressionKind.Sqrt => Math.Sqrt(_operand!.Evaluate(frame)),
            ExpressionKind.Floor => Math.Floor(_operand!.Evaluate(frame)),
            ExpressionKind.Ceil => Math.Ceiling(_operand!.Evaluate(frame)),
            ExpressionKind.Round => Math.Round(_operand!.Evaluate(frame), MidpointRounding.ToEven),
            _ => throw Unsupported()
        };

    public static bool TryCreate(
        Expression expression,
        InterpreterFrame frame,
        string targetName,
        IBindingCatalog bindings,
        out F64ExpressionPlan plan,
        out BindingDescriptor binding)
    {
        binding = null!;
        switch (expression)
        {
            case LiteralExpression { Value: F64Value value }:
                plan = new F64ExpressionPlan(
                    ExpressionKind.Literal,
                    literal: value.Value,
                    preservesNonNegative: value.Value >= 0);
                return true;
            case VariableExpression variable when frame.TryReadDouble(variable.Name, out var current):
                var slot = frame.GetSlot(variable.Name);
                plan = new F64ExpressionPlan(
                    frame.IsF64Slot(slot) ? ExpressionKind.RawVariable : ExpressionKind.BoxedVariable,
                    slot,
                    preservesNonNegative: current >= 0);
                return true;
            case CallExpression call when TryCreateUnaryBinding(call, frame, targetName, bindings, out plan, out binding):
                return true;
            default:
                plan = null!;
                return false;
        }
    }

    private static bool TryCreateUnaryBinding(
        CallExpression call,
        InterpreterFrame frame,
        string targetName,
        IBindingCatalog bindings,
        out F64ExpressionPlan plan,
        out BindingDescriptor descriptor)
    {
        plan = null!;
        descriptor = null!;
        if (call.Arguments.Count != 1 ||
            !TryGetKind(call.Name, out var kind) ||
            !CanUseDirectIntrinsic(bindings, call.Name, kind, out descriptor) ||
            !TryCreate(call.Arguments[0], frame, targetName, bindings, out var operand, out var operandBinding) ||
            operandBinding is not null &&
            !string.Equals(operandBinding.Id, descriptor.Id, StringComparison.Ordinal))
        {
            return false;
        }

        if (kind == ExpressionKind.Sqrt && !operand.PreservesNonNegative)
        {
            return false;
        }

        plan = new F64ExpressionPlan(kind, operand: operand, preservesNonNegative: operand.PreservesNonNegative);
        return true;
    }

    private static bool CanUseDirectIntrinsic(
        IBindingCatalog bindings,
        string id,
        ExpressionKind kind,
        out BindingDescriptor descriptor)
    {
        if (bindings is BindingRegistry registry &&
            registry.TryGet(id, out var binding) &&
            binding.Compiled is { Kind: "RuntimeStub" } &&
            binding.Compiled.Type == typeof(CompiledRuntime).FullName &&
            binding.Compiled.Method == RuntimeMethod(kind) &&
            binding.Parameters.Count == 1 &&
            binding.Parameters[0].Equals(SandboxType.F64) &&
            binding.ReturnType.Equals(SandboxType.F64) &&
            binding.RequiredCapability is null &&
            binding.Safety == BindingSafety.PureIntrinsic &&
            binding.AuditLevel == AuditLevel.None &&
            binding.CostModel.MaxCallsPerRun is null &&
            (binding.Effects & ~(SandboxEffect.Cpu | SandboxEffect.Alloc)) == SandboxEffect.None)
        {
            descriptor = registry.GetDescriptor(id);
            return true;
        }

        descriptor = null!;
        return false;
    }

    private static bool TryGetKind(string id, out ExpressionKind kind)
    {
        kind = id switch {
            "math.sqrt" => ExpressionKind.Sqrt,
            "math.floor" => ExpressionKind.Floor,
            "math.ceil" => ExpressionKind.Ceil,
            "math.round" => ExpressionKind.Round,
            _ => ExpressionKind.Literal
        };
        return kind is not ExpressionKind.Literal;
    }

    private static string RuntimeMethod(ExpressionKind kind)
        => kind switch {
            ExpressionKind.Sqrt => nameof(CompiledRuntime.SqrtF64),
            ExpressionKind.Floor => nameof(CompiledRuntime.FloorF64),
            ExpressionKind.Ceil => nameof(CompiledRuntime.CeilF64),
            ExpressionKind.Round => nameof(CompiledRuntime.RoundF64),
            _ => ""
        };

    private static SandboxRuntimeException Unsupported()
        => new(new SandboxError(SandboxErrorCode.ValidationError, "unsupported f64 expression"));

    private enum ExpressionKind
    {
        Literal,
        RawVariable,
        BoxedVariable,
        Sqrt,
        Floor,
        Ceil,
        Round
    }
}

namespace SafeIR.Compiler.Emitters;

using System.Reflection.Emit;
using SafeIR;
using SafeIR.Runtime;
using static SafeIR.Compiler.IlEmitterPrimitives;

internal static class F64LoopFastPathEmitter
{
    private const int LoopFuel = 5;

    public static bool TryEmit(
        ForRangeStatement range,
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        IBindingCatalog bindings,
        IReadOnlySet<string> nonNegativeF64Locals,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare,
        out string? nonNegativeTarget)
    {
        nonNegativeTarget = null;
        if (stackPlan.LocalKind(range.LocalName) != StackKind.I32 ||
            !CanEmitBound(range.Start, stackPlan) ||
            !CanEmitBound(range.End, stackPlan) ||
            range.Body.Count != 1 ||
            range.Body[0] is not AssignmentStatement assignment ||
            stackPlan.LocalKind(assignment.Name) != StackKind.F64 ||
            !ExpressionPlan.TryCreate(assignment.Value, assignment.Name, stackPlan, bindings, nonNegativeF64Locals, out var expression))
        {
            return false;
        }

        nonNegativeTarget = expression.PreservesNonNegative ? assignment.Name : null;
        EmitLoop(range, il, declare, assignment.Name, expression);
        return true;
    }

    private static void EmitLoop(
        ForRangeStatement range,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare,
        string target,
        ExpressionPlan expression)
    {
        var index = il.DeclareLocal(typeof(int));
        var end = il.DeclareLocal(typeof(int));
        var iterations = il.DeclareLocal(typeof(int));
        EmitBound(range.Start, il, declare);
        il.Emit(OpCodes.Stloc, index);
        EmitBound(range.End, il, declare);
        il.Emit(OpCodes.Stloc, end);

        var fallback = il.DefineLabel();
        var fastLoop = il.DefineLabel();
        var fallbackLoop = il.DefineLabel();
        var finish = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Bge, finish);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iterations);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, expression.BindingId);
        il.Emit(OpCodes.Ldloc, iterations);
        EmitInt32(il, expression.BindingCallCount);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.CanBulkChargeBindingCallsScaled)));
        il.Emit(OpCodes.Brfalse, fallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, expression.BindingId);
        il.Emit(OpCodes.Ldloc, iterations);
        EmitInt32(il, expression.BindingCallCount);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeBindingCallsScaled)));
        EmitLoopBody(fastLoop, finish, index, end, range.LocalName, target, expression, il, declare, chargeBinding: false);

        il.MarkLabel(fallback);
        EmitLoopBody(fallbackLoop, finish, index, end, range.LocalName, target, expression, il, declare, chargeBinding: true);
        il.MarkLabel(finish);
    }

    private static void EmitLoopBody(
        Label loop,
        Label finish,
        LocalBuilder index,
        LocalBuilder end,
        string loopLocal,
        string target,
        ExpressionPlan expression,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare,
        bool chargeBinding)
    {
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Bge, finish);
        CompiledMeterEmitter.LoopIteration(il, LoopFuel + 1 + expression.FuelCost);
        if (chargeBinding)
        {
            for (var i = 0; i < expression.BindingCallCount; i++)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, expression.BindingId);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeBindingCall)));
            }
        }

        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Stloc, declare(loopLocal).Local);
        expression.Emit(il, declare);
        il.Emit(OpCodes.Stloc, declare(target).Local);
        il.Emit(OpCodes.Ldloc, index);
        EmitInt32(il, 1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
    }

    private static void EmitBound(Expression expression, ILGenerator il, Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        switch (expression)
        {
            case LiteralExpression { Value: I32Value value }:
                EmitInt32(il, value.Value);
                break;
            case VariableExpression variable:
                il.Emit(OpCodes.Ldloc, declare(variable.Name).Local);
                break;
            default:
                throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported forRange bound"));
        }
    }

    private static bool CanEmitBound(Expression expression, LocalStackKindPlanner stackPlan)
        => expression is LiteralExpression { Value: I32Value } ||
           expression is VariableExpression variable && stackPlan.LocalKind(variable.Name) == StackKind.I32;

    private sealed class ExpressionPlan
    {
        private readonly ExpressionKind _kind;
        private readonly string? _name;
        private readonly double _literal;
        private readonly ExpressionPlan? _operand;

        private ExpressionPlan(ExpressionKind kind, string bindingId = "", string? name = null, double literal = 0, ExpressionPlan? operand = null, bool preservesNonNegative = false)
        {
            _kind = kind;
            _name = name;
            _literal = literal;
            _operand = operand;
            BindingId = bindingId;
            PreservesNonNegative = preservesNonNegative;
            FuelCost = 1 + (operand?.FuelCost ?? 0);
            BindingCallCount = kind is ExpressionKind.Literal or ExpressionKind.Variable
                ? 0
                : 1 + (operand?.BindingCallCount ?? 0);
        }

        public string BindingId { get; }
        public int BindingCallCount { get; }
        public bool PreservesNonNegative { get; }
        public int FuelCost { get; }

        public static bool TryCreate(Expression expression, string target, LocalStackKindPlanner stackPlan, IBindingCatalog bindings, IReadOnlySet<string> nonNegativeF64Locals, out ExpressionPlan plan)
        {
            switch (expression)
            {
                case LiteralExpression { Value: F64Value value }:
                    plan = new ExpressionPlan(ExpressionKind.Literal, literal: value.Value, preservesNonNegative: value.Value >= 0);
                    return true;
                case VariableExpression variable when stackPlan.LocalKind(variable.Name) == StackKind.F64:
                    plan = new ExpressionPlan(ExpressionKind.Variable, name: variable.Name, preservesNonNegative: nonNegativeF64Locals.Contains(variable.Name));
                    return true;
                case CallExpression call:
                    return TryCreateCall(call, target, stackPlan, bindings, nonNegativeF64Locals, out plan);
                default:
                    plan = null!;
                    return false;
            }
        }

        public void Emit(ILGenerator il, Func<string, (LocalBuilder Local, StackKind Kind)> declare)
        {
            switch (_kind)
            {
                case ExpressionKind.Literal:
                    il.Emit(OpCodes.Ldc_R8, _literal);
                    break;
                case ExpressionKind.Variable:
                    il.Emit(OpCodes.Ldloc, declare(_name!).Local);
                    break;
                default:
                    _operand!.Emit(il, declare);
                    il.Emit(OpCodes.Call, Runtime(RawMethod(_kind)));
                    break;
            }
        }

        private static bool TryCreateCall(CallExpression call, string target, LocalStackKindPlanner stackPlan, IBindingCatalog bindings, IReadOnlySet<string> nonNegativeF64Locals, out ExpressionPlan plan)
        {
            plan = null!;
            if (call.Arguments.Count != 1 ||
                !TryGetKind(call.Name, out var kind) ||
                !CanUseDirectIntrinsic(bindings, call.Name, kind) ||
                !TryCreate(call.Arguments[0], target, stackPlan, bindings, nonNegativeF64Locals, out var operand) ||
                operand.BindingCallCount > 0 &&
                !string.Equals(operand.BindingId, call.Name, StringComparison.Ordinal))
            {
                return false;
            }

            if (kind == ExpressionKind.Sqrt && !operand.PreservesNonNegative)
            {
                return false;
            }

            plan = new ExpressionPlan(kind, bindingId: call.Name, operand: operand, preservesNonNegative: operand.PreservesNonNegative);
            return true;
        }

        private static bool CanUseDirectIntrinsic(IBindingCatalog bindings, string id, ExpressionKind kind)
            => bindings.TryGet(id, out var binding) &&
               binding.Compiled is { Kind: "RuntimeStub" } &&
               binding.Compiled.Type == typeof(CompiledRuntime).FullName &&
               binding.Compiled.Method == BoxedMethod(kind) &&
               binding.Parameters.Count == 1 &&
               binding.Parameters[0].Equals(SandboxType.F64) &&
               binding.ReturnType.Equals(SandboxType.F64) &&
               binding.RequiredCapability is null &&
               binding.Safety == BindingSafety.PureIntrinsic &&
               binding.AuditLevel == AuditLevel.None &&
               binding.CostModel.MaxCallsPerRun is null &&
               (binding.Effects & ~(SandboxEffect.Cpu | SandboxEffect.Alloc)) == SandboxEffect.None;

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

        private static string BoxedMethod(ExpressionKind kind)
            => kind switch {
                ExpressionKind.Sqrt => nameof(CompiledRuntime.SqrtF64),
                ExpressionKind.Floor => nameof(CompiledRuntime.FloorF64),
                ExpressionKind.Ceil => nameof(CompiledRuntime.CeilF64),
                ExpressionKind.Round => nameof(CompiledRuntime.RoundF64),
                _ => ""
            };

        private static string RawMethod(ExpressionKind kind)
            => kind switch {
                ExpressionKind.Sqrt => nameof(CompiledRuntime.SqrtF64Raw),
                ExpressionKind.Floor => nameof(CompiledRuntime.FloorF64Raw),
                ExpressionKind.Ceil => nameof(CompiledRuntime.CeilF64Raw),
                ExpressionKind.Round => nameof(CompiledRuntime.RoundF64Raw),
                _ => ""
            };
    }

    private enum ExpressionKind { Literal, Variable, Sqrt, Floor, Ceil, Round }
}

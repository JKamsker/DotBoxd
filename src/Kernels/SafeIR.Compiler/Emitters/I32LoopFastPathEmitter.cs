namespace SafeIR.Compiler.Emitters;

using System.Reflection.Emit;
using SafeIR;
using SafeIR.Runtime;
using static SafeIR.Compiler.IlEmitterPrimitives;

internal sealed class I32LoopFastPathEmitter
{
    private const int LoopFuel = 5;
    private const int MaxEstimatedInstructionsBetweenMeters = 30;

    private readonly ILGenerator _il;
    private readonly LocalStackKindPlanner _stackPlan;
    private readonly ExpressionEmitter _expressions;
    private readonly IReadOnlyDictionary<string, SandboxFunction> _functions;
    private readonly Func<string, (LocalBuilder Local, StackKind Kind)> _declare;

    private I32LoopFastPathEmitter(ILGenerator il, LocalStackKindPlanner stackPlan, ExpressionEmitter expressions, IReadOnlyDictionary<string, SandboxFunction> functions, Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        _il = il;
        _stackPlan = stackPlan;
        _expressions = expressions;
        _functions = functions;
        _declare = declare;
    }

    public static bool TryEmit(ForRangeStatement range, ILGenerator il, LocalStackKindPlanner stackPlan, ExpressionEmitter expressions, IReadOnlyDictionary<string, SandboxFunction> functions, Func<string, (LocalBuilder Local, StackKind Kind)> declare)
        => new I32LoopFastPathEmitter(il, stackPlan, expressions, functions, declare).TryEmit(range);

    private bool TryEmit(ForRangeStatement range)
    {
        if (_stackPlan.LocalKind(range.LocalName) != StackKind.I32 ||
            !TryCreateBodyPlan(range, out var body, out var fuelPerIteration, out var bodyInstructionCost) ||
            EstimatedLoopInstructionCost(bodyInstructionCost) > MaxEstimatedInstructionsBetweenMeters)
        {
            return false;
        }

        var index = _il.DeclareLocal(typeof(int));
        var end = _il.DeclareLocal(typeof(int));
        _expressions.EmitAs(range.Start, StackKind.I32);
        _il.Emit(OpCodes.Stloc, index);
        _expressions.EmitAs(range.End, StackKind.I32);
        _il.Emit(OpCodes.Stloc, end);

        var startLabel = _il.DefineLabel();
        var finishLabel = _il.DefineLabel();
        _il.MarkLabel(startLabel);
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Ldloc, end);
        _il.Emit(OpCodes.Bge, finishLabel);
        CompiledMeterEmitter.LoopIteration(_il, fuelPerIteration);

        var (loopVar, _) = _declare(range.LocalName);
        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Stloc, loopVar);

        for (var i = 0; i < body.Length; i++)
        {
            var assignment = body[i];
            assignment.Expression.Emit(_il, _declare);
            _il.Emit(OpCodes.Stloc, _declare(assignment.Target).Local);
        }

        _il.Emit(OpCodes.Ldloc, index);
        EmitInt32(_il, 1);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Stloc, index);
        _il.Emit(OpCodes.Br, startLabel);
        _il.MarkLabel(finishLabel);
        return true;
    }

    private bool TryCreateBodyPlan(ForRangeStatement range, out AssignmentPlan[] body, out int fuelPerIteration, out int instructionCost)
    {
        body = new AssignmentPlan[range.Body.Count];
        fuelPerIteration = LoopFuel;
        instructionCost = 0;

        for (var i = 0; i < range.Body.Count; i++)
        {
            if (range.Body[i] is not AssignmentStatement assignment ||
                _stackPlan.LocalKind(assignment.Name) != StackKind.I32 ||
                !ExpressionPlan.TryCreate(assignment.Value, _stackPlan, _functions, out var expression))
            {
                body = [];
                return false;
            }

            body[i] = new AssignmentPlan(assignment.Name, expression);
            fuelPerIteration += 1 + expression.FuelCost;
            instructionCost += 1 + expression.InstructionCost;
        }

        return true;
    }

    private static int EstimatedLoopInstructionCost(int bodyInstructionCost)
        // loop local store, body, index increment, back edge, condition, and next meter arguments.
        => 2 + bodyInstructionCost + 4 + 1 + 3 + 2;

    private readonly record struct AssignmentPlan(string Target, ExpressionPlan Expression);

    private sealed class ExpressionPlan
    {
        private static readonly IReadOnlyDictionary<string, SandboxFunction> NoFunctions = new Dictionary<string, SandboxFunction>(StringComparer.Ordinal);

        private readonly ExpressionKind _kind;
        private readonly string? _name;
        private readonly int _literal;
        private readonly ExpressionPlan? _left;
        private readonly ExpressionPlan? _right;
        private readonly ExpressionPlan? _third;

        private ExpressionPlan(ExpressionKind kind, string? name = null, int literal = 0, ExpressionPlan? left = null, ExpressionPlan? right = null, ExpressionPlan? third = null, int extraFuel = 0)
        {
            _kind = kind;
            _name = name;
            _literal = literal;
            _left = left;
            _right = right;
            _third = third;
            FuelCost = 1 + extraFuel + (left?.FuelCost ?? 0) + (right?.FuelCost ?? 0) + (third?.FuelCost ?? 0);
            InstructionCost = kind is ExpressionKind.Literal or ExpressionKind.Variable
                ? 1
                : 1 + (left?.InstructionCost ?? 0) + (right?.InstructionCost ?? 0) + (third?.InstructionCost ?? 0);
        }

        public int FuelCost { get; }

        public int InstructionCost { get; }

        public static bool TryCreate(Expression expression, LocalStackKindPlanner stackPlan, IReadOnlyDictionary<string, SandboxFunction> functions, out ExpressionPlan plan)
            => TryCreate(expression, stackPlan, functions, substitutions: null, out plan);

        private static bool TryCreate(Expression expression, LocalStackKindPlanner stackPlan, IReadOnlyDictionary<string, SandboxFunction> functions, IReadOnlyDictionary<string, ExpressionPlan>? substitutions, out ExpressionPlan plan)
        {
            switch (expression)
            {
                case LiteralExpression { Value: I32Value value }:
                    plan = new ExpressionPlan(ExpressionKind.Literal, literal: value.Value);
                    return true;
                case VariableExpression variable when substitutions?.TryGetValue(variable.Name, out var substitution) == true:
                    plan = substitution;
                    return true;
                case VariableExpression variable when stackPlan.LocalKind(variable.Name) == StackKind.I32:
                    plan = new ExpressionPlan(ExpressionKind.Variable, name: variable.Name);
                    return true;
                case UnaryExpression { Operator: "-" } unary
                    when TryCreate(unary.Operand, stackPlan, functions, substitutions, out var operand):
                    plan = new ExpressionPlan(ExpressionKind.Negate, left: operand);
                    return true;
                case BinaryExpression binary when binary.Operator is "+" or "-" or "*" or "/" or "%":
                    return TryCreateAddRemainder(binary, stackPlan, functions, substitutions, out plan) ||
                           TryCreateBinary(binary, stackPlan, functions, substitutions, out plan);
                case CallExpression call:
                    return TryCreateInlineCall(call, stackPlan, functions, out plan);
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
                    EmitInt32(il, _literal);
                    break;
                case ExpressionKind.Variable:
                    il.Emit(OpCodes.Ldloc, declare(_name!).Local);
                    break;
                case ExpressionKind.Negate:
                    _left!.Emit(il, declare);
                    il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.NegI32Raw)));
                    break;
                case ExpressionKind.InlineCall:
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.EnterInlineCall)));
                    _left!.Emit(il, declare);
                    var value = il.DeclareLocal(typeof(int));
                    il.Emit(OpCodes.Stloc, value);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ExitInlineCall)));
                    il.Emit(OpCodes.Ldloc, value);
                    break;
                case ExpressionKind.Add:
                case ExpressionKind.Subtract:
                case ExpressionKind.Multiply:
                case ExpressionKind.Divide:
                case ExpressionKind.Remainder:
                    _left!.Emit(il, declare);
                    _right!.Emit(il, declare);
                    il.Emit(OpCodes.Call, Runtime(RuntimeMethod(_kind)));
                    break;
                case ExpressionKind.AddRemainder:
                    _left!.Emit(il, declare);
                    _right!.Emit(il, declare);
                    _third!.Emit(il, declare);
                    il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddRemI32Raw)));
                    break;
            }
        }

        private static bool TryCreateAddRemainder(BinaryExpression binary, LocalStackKindPlanner stackPlan, IReadOnlyDictionary<string, SandboxFunction> functions, IReadOnlyDictionary<string, ExpressionPlan>? substitutions, out ExpressionPlan plan)
        {
            if (substitutions is not null)
            {
                plan = null!;
                return false;
            }

            if (binary is not
                {
                    Operator: "%",
                    Left: BinaryExpression { Operator: "+" } add
                } ||
                !TryCreate(add.Left, stackPlan, functions, out var left) ||
                !TryCreate(add.Right, stackPlan, functions, out var right) ||
                !TryCreate(binary.Right, stackPlan, functions, out var divisor))
            {
                plan = null!;
                return false;
            }

            plan = new ExpressionPlan(ExpressionKind.AddRemainder, left: left, right: right, third: divisor, extraFuel: 1);
            return true;
        }

        private static bool TryCreateBinary(BinaryExpression binary, LocalStackKindPlanner stackPlan, IReadOnlyDictionary<string, SandboxFunction> functions, IReadOnlyDictionary<string, ExpressionPlan>? substitutions, out ExpressionPlan plan)
        {
            if (!TryCreate(binary.Left, stackPlan, functions, substitutions, out var left) ||
                !TryCreate(binary.Right, stackPlan, functions, substitutions, out var right))
            {
                plan = null!;
                return false;
            }

            plan = new ExpressionPlan(BinaryKind(binary.Operator), left: left, right: right);
            return true;
        }

        private static bool TryCreateInlineCall(CallExpression call, LocalStackKindPlanner stackPlan, IReadOnlyDictionary<string, SandboxFunction> functions, out ExpressionPlan plan)
        {
            plan = null!;
            if (!functions.TryGetValue(call.Name, out var function) ||
                !TryGetInlineableInt32Return(function, call, out var expression) ||
                !TryCreate(call.Arguments[0], stackPlan, functions, out var argument))
            {
                return false;
            }

            var substitutions = new Dictionary<string, ExpressionPlan>(StringComparer.Ordinal) { [function.Parameters[0].Name] = argument };
            if (TryCreate(expression, stackPlan, NoFunctions, substitutions, out var body))
            {
                plan = new ExpressionPlan(ExpressionKind.InlineCall, left: body, extraFuel: 3);
                return true;
            }

            return false;
        }

        private static bool TryGetInlineableInt32Return(SandboxFunction function, CallExpression call, out Expression expression)
        {
            var inlineable = call.Arguments.Count == 1 &&
                             IsSimpleInlineArgument(call.Arguments[0]) &&
                             function.Parameters.Count == 1 &&
                             function.Parameters[0].Type == SandboxType.I32 &&
                             function.ReturnType == SandboxType.I32 &&
                             function.Body.Count == 1 &&
                             function.Body[0] is ReturnStatement ret &&
                             !ContainsCall(ret.Value) &&
                             CountVariableUses(ret.Value, function.Parameters[0].Name) == 1;
            expression = inlineable ? ((ReturnStatement)function.Body[0]).Value : null!;
            return inlineable;
        }

        private static int CountVariableUses(Expression expression, string name)
            => expression switch {
                VariableExpression variable => string.Equals(variable.Name, name, StringComparison.Ordinal) ? 1 : 0,
                UnaryExpression unary => CountVariableUses(unary.Operand, name),
                BinaryExpression binary => CountVariableUses(binary.Left, name) + CountVariableUses(binary.Right, name),
                CallExpression call => call.Arguments.Sum(argument => CountVariableUses(argument, name)),
                _ => 0
            };

        private static bool IsSimpleInlineArgument(Expression expression)
            => expression is LiteralExpression { Value: I32Value } or VariableExpression;

        private static bool ContainsCall(Expression expression)
            => expression switch {
                CallExpression => true,
                UnaryExpression unary => ContainsCall(unary.Operand),
                BinaryExpression binary => ContainsCall(binary.Left) || ContainsCall(binary.Right),
                _ => false
            };

        private static ExpressionKind BinaryKind(string op)
            => op switch
            {
                "+" => ExpressionKind.Add,
                "-" => ExpressionKind.Subtract,
                "*" => ExpressionKind.Multiply,
                "/" => ExpressionKind.Divide,
                "%" => ExpressionKind.Remainder,
                _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i32 expression"))
            };

        private static string RuntimeMethod(ExpressionKind kind)
            => kind switch
            {
                ExpressionKind.Add => nameof(CompiledRuntime.AddI32Raw),
                ExpressionKind.Subtract => nameof(CompiledRuntime.SubI32Raw),
                ExpressionKind.Multiply => nameof(CompiledRuntime.MulI32Raw),
                ExpressionKind.Divide => nameof(CompiledRuntime.DivI32Raw),
                ExpressionKind.Remainder => nameof(CompiledRuntime.RemI32Raw),
                _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i32 expression"))
            };
    }

    private enum ExpressionKind
    {
        Literal,
        Variable,
        Negate,
        InlineCall,
        Add,
        Subtract,
        Multiply,
        Divide,
        Remainder,
        AddRemainder
    }
}

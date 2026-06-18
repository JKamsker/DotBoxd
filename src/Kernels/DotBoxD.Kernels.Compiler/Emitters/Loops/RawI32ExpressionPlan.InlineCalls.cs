using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters.Loops;

internal sealed partial class RawI32ExpressionPlan
{
    private static bool TryCreateInlineCall(
        CallExpression call,
        LocalStackKindPlanner stackPlan,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        IBindingCatalog? bindings,
        out RawI32ExpressionPlan plan)
    {
        plan = null!;
        if (!functions.TryGetValue(call.Name, out var function) ||
            !TryGetInlineableInt32Return(function, call, out var expression) ||
            !TryCreate(call.Arguments[0], stackPlan, functions, bindings, substitutions: null, out var argument))
        {
            return false;
        }

        var substitutions = new Dictionary<string, RawI32ExpressionPlan>(StringComparer.Ordinal) { [function.Parameters[0].Name] = argument };
        if (TryCreate(expression, stackPlan, NoFunctions, bindings: null, substitutions, out var body))
        {
            plan = new RawI32ExpressionPlan(
                ExpressionKind.InlineCall,
                left: body,
                extraFuel: 3);
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
}

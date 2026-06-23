using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter;

internal sealed partial class ExpressionEvaluator
{
    private ValueTask<SandboxValue> EvaluateUnary(UnaryExpression unary, InterpreterFrame frame)
    {
        var operand = EvaluateAsync(unary.Operand, frame);
        return operand.IsCompletedSuccessfully
            ? new ValueTask<SandboxValue>(OperatorEvaluator.ApplyUnary(unary, operand.Result))
            : AwaitUnary(unary, operand);
    }

    private async ValueTask<SandboxValue> AwaitUnary(UnaryExpression unary, ValueTask<SandboxValue> operand)
        => OperatorEvaluator.ApplyUnary(unary, await operand.ConfigureAwait(false));

    private ValueTask<SandboxValue> EvaluateBinary(BinaryExpression binary, InterpreterFrame frame)
    {
        if (binary.Operator is "&&" or "||")
        {
            return EvaluateShortCircuit(binary, frame);
        }

        var leftTask = EvaluateAsync(binary.Left, frame);
        if (!leftTask.IsCompletedSuccessfully)
        {
            return AwaitBinary(binary, leftTask, frame);
        }

        var rightTask = EvaluateAsync(binary.Right, frame);
        if (!rightTask.IsCompletedSuccessfully)
        {
            return AwaitBinaryRight(binary, leftTask.Result, rightTask);
        }

        return new ValueTask<SandboxValue>(OperatorEvaluator.ApplyBinary(binary, leftTask.Result, rightTask.Result, _context));
    }

    private async ValueTask<SandboxValue> AwaitBinary(
        BinaryExpression binary,
        ValueTask<SandboxValue> leftTask,
        InterpreterFrame frame)
    {
        var left = await leftTask.ConfigureAwait(false);
        var right = await EvaluateAsync(binary.Right, frame).ConfigureAwait(false);
        return OperatorEvaluator.ApplyBinary(binary, left, right, _context);
    }

    private async ValueTask<SandboxValue> AwaitBinaryRight(
        BinaryExpression binary,
        SandboxValue left,
        ValueTask<SandboxValue> rightTask)
        => OperatorEvaluator.ApplyBinary(binary, left, await rightTask.ConfigureAwait(false), _context);

    private ValueTask<SandboxValue> EvaluateShortCircuit(BinaryExpression binary, InterpreterFrame frame)
    {
        var shortCircuitOn = binary.Operator == "||";
        var order = ShortCircuitExpressionOrder.Choose(binary, _context.Bindings, _functionAnalysis);
        var firstTask = EvaluateAsync(order.First, frame);
        if (!firstTask.IsCompletedSuccessfully)
        {
            return AwaitShortCircuit(order, shortCircuitOn, firstTask, frame);
        }

        if (((BoolValue)firstTask.Result).Value == shortCircuitOn)
        {
            return new ValueTask<SandboxValue>(SandboxValue.FromBool(shortCircuitOn));
        }

        var secondTask = EvaluateAsync(order.Second, frame);
        return secondTask.IsCompletedSuccessfully
            ? new ValueTask<SandboxValue>(SandboxValue.FromBool(((BoolValue)secondTask.Result).Value))
            : AwaitShortCircuitSecond(secondTask);
    }

    private async ValueTask<SandboxValue> AwaitShortCircuit(
        ShortCircuitOperands order,
        bool shortCircuitOn,
        ValueTask<SandboxValue> firstTask,
        InterpreterFrame frame)
    {
        var first = (BoolValue)await firstTask.ConfigureAwait(false);
        if (first.Value == shortCircuitOn)
        {
            return SandboxValue.FromBool(shortCircuitOn);
        }

        var second = (BoolValue)await EvaluateAsync(order.Second, frame).ConfigureAwait(false);
        return SandboxValue.FromBool(second.Value);
    }

    private static async ValueTask<SandboxValue> AwaitShortCircuitSecond(ValueTask<SandboxValue> secondTask)
        => SandboxValue.FromBool(((BoolValue)await secondTask.ConfigureAwait(false)).Value);
}

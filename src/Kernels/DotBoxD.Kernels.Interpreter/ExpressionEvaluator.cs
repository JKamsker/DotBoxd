using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter;

using DotBoxD.Kernels;

internal sealed partial class ExpressionEvaluator
{
    private readonly SandboxContext _context;
    private readonly InterpreterEvaluator _interpreter;
    private readonly IReadOnlyDictionary<string, FunctionAnalysis> _functionAnalysis;
    private readonly SandboxExecutionOptions _options;
    private readonly string _moduleHash;

    public ExpressionEvaluator(
        SandboxContext context,
        InterpreterEvaluator interpreter,
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis,
        SandboxExecutionOptions options,
        string moduleHash)
    {
        _context = context;
        _interpreter = interpreter;
        _functionAnalysis = functionAnalysis;
        _options = options;
        _moduleHash = moduleHash;
    }

    // Non-async dispatch: literals, variables, arithmetic, and pure helper calls all
    // complete synchronously, so they return a finished ValueTask without ever
    // allocating an async state machine. Only genuinely asynchronous work (a host
    // binding whose ValueTask is still pending) walks the async continuation path.
    public ValueTask<SandboxValue> EvaluateAsync(Expression expression, InterpreterFrame frame)
    {
        if (!_options.EnableDebugTrace &&
            I32ExpressionEvaluator.CanEvaluate(expression, frame, _interpreter))
        {
            return new ValueTask<SandboxValue>(
                SandboxValue.FromInt32(I32ExpressionEvaluator.Evaluate(expression, frame, _context, _interpreter)));
        }

        _context.ChargeFuel(1);
        InterpreterTrace.Write(
            _context,
            _options,
            _moduleHash,
            frame.FunctionId,
            "expression",
            expression.GetType().Name,
            expression.Span);
        return expression switch
        {
            LiteralExpression literal => new ValueTask<SandboxValue>(ChargeLiteral(literal.Value)),
            VariableExpression variable => new ValueTask<SandboxValue>(frame.Read(variable.Name)),
            UnaryExpression unary => EvaluateUnary(unary, frame),
            BinaryExpression binary => EvaluateBinary(binary, frame),
            CallExpression call => EvaluateCall(call, frame),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported expression"))
        };
    }

    public bool TryEvaluateInt32(Expression expression, InterpreterFrame frame, out int value)
    {
        if (!_options.EnableDebugTrace && I32ExpressionEvaluator.CanEvaluate(expression, frame, _interpreter))
        {
            value = I32ExpressionEvaluator.Evaluate(expression, frame, _context, _interpreter);
            return true;
        }

        value = 0;
        return false;
    }

    public ValueTask<SandboxValue> InvokeFunctionAsync(SandboxFunction function, IReadOnlyList<SandboxValue> args)
        => _interpreter.InvokeFunctionAsync(function, args);

    private SandboxValue ChargeLiteral(SandboxValue value)
    {
        _context.ChargeValue(value);
        return value;
    }

}

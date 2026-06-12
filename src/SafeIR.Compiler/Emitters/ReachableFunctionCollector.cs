namespace SafeIR.Compiler.Emitters;

using SafeIR;

internal static class ReachableFunctionCollector
{
    public static IReadOnlyList<SandboxFunction> Collect(ExecutionPlan plan, SandboxFunction entrypoint)
    {
        var functions = plan.Module.Functions.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var reachable = new List<SandboxFunction>();
        CollectFunction(entrypoint, functions, visited, reachable);
        return reachable;
    }

    private static void CollectFunction(
        SandboxFunction function,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        HashSet<string> visited,
        List<SandboxFunction> reachable)
    {
        if (!visited.Add(function.Id))
        {
            return;
        }

        reachable.Add(function);
        CollectBlock(function.Body, functions, visited, reachable);
    }

    private static bool CollectBlock(
        IReadOnlyList<Statement> statements,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        HashSet<string> visited,
        List<SandboxFunction> reachable)
    {
        var alwaysReturns = false;
        foreach (var statement in statements)
        {
            if (!alwaysReturns)
            {
                alwaysReturns = CollectStatement(statement, functions, visited, reachable);
            }
        }

        return alwaysReturns;
    }

    private static bool CollectStatement(
        Statement statement,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        HashSet<string> visited,
        List<SandboxFunction> reachable)
        => statement switch
        {
            AssignmentStatement assignment => CollectExpressionStatement(assignment.Value, functions, visited, reachable),
            ReturnStatement ret => CollectReturn(ret.Value, functions, visited, reachable),
            ExpressionStatement expression => CollectExpressionStatement(expression.Value, functions, visited, reachable),
            IfStatement branch => CollectBranch(branch, functions, visited, reachable),
            WhileStatement loop => CollectLoop(loop, functions, visited, reachable),
            ForRangeStatement range => CollectRange(range, functions, visited, reachable),
            _ => false
        };

    private static bool CollectExpressionStatement(
        Expression expression,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        HashSet<string> visited,
        List<SandboxFunction> reachable)
    {
        CollectExpression(expression, functions, visited, reachable);
        return false;
    }

    private static bool CollectReturn(
        Expression expression,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        HashSet<string> visited,
        List<SandboxFunction> reachable)
    {
        CollectExpression(expression, functions, visited, reachable);
        return true;
    }

    private static bool CollectBranch(
        IfStatement branch,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        HashSet<string> visited,
        List<SandboxFunction> reachable)
    {
        CollectExpression(branch.Condition, functions, visited, reachable);
        var thenReturns = CollectBlock(branch.Then, functions, visited, reachable);
        var elseReturns = CollectBlock(branch.Else, functions, visited, reachable);
        return thenReturns && elseReturns;
    }

    private static bool CollectLoop(
        WhileStatement loop,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        HashSet<string> visited,
        List<SandboxFunction> reachable)
    {
        CollectExpression(loop.Condition, functions, visited, reachable);
        CollectBlock(loop.Body, functions, visited, reachable);
        return false;
    }

    private static bool CollectRange(
        ForRangeStatement range,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        HashSet<string> visited,
        List<SandboxFunction> reachable)
    {
        CollectExpression(range.Start, functions, visited, reachable);
        CollectExpression(range.End, functions, visited, reachable);
        CollectBlock(range.Body, functions, visited, reachable);
        return false;
    }

    private static void CollectExpression(
        Expression expression,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        HashSet<string> visited,
        List<SandboxFunction> reachable)
    {
        switch (expression)
        {
            case CallExpression call:
                foreach (var argument in call.Arguments)
                {
                    CollectExpression(argument, functions, visited, reachable);
                }

                if (functions.TryGetValue(call.Name, out var function))
                {
                    CollectFunction(function, functions, visited, reachable);
                }

                break;
            case UnaryExpression unary:
                CollectExpression(unary.Operand, functions, visited, reachable);
                break;
            case BinaryExpression binary:
                CollectExpression(binary.Left, functions, visited, reachable);
                CollectExpression(binary.Right, functions, visited, reachable);
                break;
        }
    }
}

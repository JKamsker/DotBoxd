namespace SafeIR;

public static class BindingReferenceCollector
{
    public static IReadOnlySet<string> Collect(SandboxModule module, IBindingCatalog bindings)
        => Collect(module, bindings, entrypoint: null);

    public static IReadOnlySet<string> Collect(SandboxModule module, IBindingCatalog bindings, string? entrypoint)
    {
        var functions = module.Functions.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        if (entrypoint is not null) {
            if (functions.TryGetValue(entrypoint, out var function)) {
                CollectFunction(function, functions, bindings, ids, new HashSet<string>(StringComparer.Ordinal));
            }

            return ids;
        }

        foreach (var function in functions.Values) {
            CollectFunction(function, functions, bindings, ids, new HashSet<string>(StringComparer.Ordinal));
        }

        return ids;
    }

    private static void CollectFunction(
        SandboxFunction function,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        IBindingCatalog bindings,
        HashSet<string> ids,
        HashSet<string> visited)
    {
        if (!visited.Add(function.Id)) {
            return;
        }

        CollectBlock(function.Body, functions, bindings, ids, visited);
    }

    private static bool CollectStatement(
        Statement statement,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        IBindingCatalog bindings,
        HashSet<string> ids,
        HashSet<string> visited)
    {
        switch (statement) {
            case AssignmentStatement assignment:
                CollectExpression(assignment.Value, functions, bindings, ids, visited);
                return false;
            case ReturnStatement ret:
                CollectExpression(ret.Value, functions, bindings, ids, visited);
                return true;
            case ExpressionStatement expression:
                CollectExpression(expression.Value, functions, bindings, ids, visited);
                return false;
            case IfStatement branch:
                CollectExpression(branch.Condition, functions, bindings, ids, visited);
                var thenReturns = CollectBlock(branch.Then, functions, bindings, ids, visited);
                var elseReturns = CollectBlock(branch.Else, functions, bindings, ids, visited);
                return thenReturns && elseReturns;
            case WhileStatement loop:
                CollectExpression(loop.Condition, functions, bindings, ids, visited);
                CollectBlock(loop.Body, functions, bindings, ids, visited);
                return false;
            case ForRangeStatement range:
                CollectExpression(range.Start, functions, bindings, ids, visited);
                CollectExpression(range.End, functions, bindings, ids, visited);
                CollectBlock(range.Body, functions, bindings, ids, visited);
                return false;
            default:
                return false;
        }
    }

    private static bool CollectBlock(
        IReadOnlyList<Statement> statements,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        IBindingCatalog bindings,
        HashSet<string> ids,
        HashSet<string> visited)
    {
        var alwaysReturns = false;
        foreach (var statement in statements) {
            if (alwaysReturns) {
                continue;
            }

            alwaysReturns = CollectStatement(statement, functions, bindings, ids, visited);
        }

        return alwaysReturns;
    }

    private static void CollectExpression(
        Expression expression,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        IBindingCatalog bindings,
        HashSet<string> ids,
        HashSet<string> visited)
    {
        if (expression is CallExpression call) {
            foreach (var argument in call.Arguments) {
                CollectExpression(argument, functions, bindings, ids, visited);
            }

            if (IsCollectionCall(call.Name)) {
                return;
            }

            if (functions.TryGetValue(call.Name, out var function)) {
                CollectFunction(function, functions, bindings, ids, visited);
            }
            else if (bindings.TryGet(call.Name, out _)) {
                ids.Add(call.Name);
            }
        }
        else if (expression is UnaryExpression unary) {
            CollectExpression(unary.Operand, functions, bindings, ids, visited);
        }
        else if (expression is BinaryExpression binary) {
            CollectExpression(binary.Left, functions, bindings, ids, visited);
            CollectExpression(binary.Right, functions, bindings, ids, visited);
        }
    }

    private static bool IsCollectionCall(string name)
        => name is "list.empty" or "list.of" or "list.count" or "list.get" or "list.add"
            or "map.empty" or "map.containsKey" or "map.get" or "map.set" or "map.remove";
}

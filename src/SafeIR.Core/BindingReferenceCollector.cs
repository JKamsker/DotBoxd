namespace SafeIR;

public static class BindingReferenceCollector
{
    public static IReadOnlySet<string> Collect(SandboxModule module, IBindingCatalog bindings)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in module.Functions) {
            foreach (var statement in function.Body) {
                CollectStatement(statement, bindings, ids);
            }
        }

        return ids;
    }

    private static void CollectStatement(
        Statement statement,
        IBindingCatalog bindings,
        HashSet<string> ids)
    {
        switch (statement) {
            case AssignmentStatement assignment:
                CollectExpression(assignment.Value, bindings, ids);
                break;
            case ReturnStatement ret:
                CollectExpression(ret.Value, bindings, ids);
                break;
            case ExpressionStatement expression:
                CollectExpression(expression.Value, bindings, ids);
                break;
            case IfStatement branch:
                CollectExpression(branch.Condition, bindings, ids);
                CollectBlock(branch.Then, bindings, ids);
                CollectBlock(branch.Else, bindings, ids);
                break;
            case WhileStatement loop:
                CollectExpression(loop.Condition, bindings, ids);
                CollectBlock(loop.Body, bindings, ids);
                break;
            case ForRangeStatement range:
                CollectExpression(range.Start, bindings, ids);
                CollectExpression(range.End, bindings, ids);
                CollectBlock(range.Body, bindings, ids);
                break;
        }
    }

    private static void CollectBlock(
        IReadOnlyList<Statement> statements,
        IBindingCatalog bindings,
        HashSet<string> ids)
    {
        foreach (var statement in statements) {
            CollectStatement(statement, bindings, ids);
        }
    }

    private static void CollectExpression(
        Expression expression,
        IBindingCatalog bindings,
        HashSet<string> ids)
    {
        if (expression is CallExpression call) {
            if (bindings.TryGet(call.Name, out _)) {
                ids.Add(call.Name);
            }

            foreach (var argument in call.Arguments) {
                CollectExpression(argument, bindings, ids);
            }
        }
        else if (expression is UnaryExpression unary) {
            CollectExpression(unary.Operand, bindings, ids);
        }
        else if (expression is BinaryExpression binary) {
            CollectExpression(binary.Left, bindings, ids);
            CollectExpression(binary.Right, bindings, ids);
        }
    }
}

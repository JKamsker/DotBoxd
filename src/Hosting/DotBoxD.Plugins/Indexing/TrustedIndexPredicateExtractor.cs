using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Indexing;

internal static class TrustedIndexPredicateExtractor
{
    public static IReadOnlyList<IndexedPredicate> Extract(
        PluginPackage package,
        IReadOnlyList<Parameter> eventParameters)
    {
        if (package.Module.Functions.FirstOrDefault(f =>
                string.Equals(f.Id, package.Entrypoints.ShouldHandle, StringComparison.Ordinal)) is not { } shouldHandle)
        {
            return [];
        }

        var eventPaths = EventParameterPaths(eventParameters);
        if (eventPaths.Count == 0)
        {
            return [];
        }

        var predicates = new List<IndexedPredicate>();
        CollectNecessaryPredicates(shouldHandle.Body, eventPaths, predicates);
        return predicates;
    }

    private static Dictionary<string, string> EventParameterPaths(IReadOnlyList<Parameter> eventParameters)
    {
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in eventParameters)
        {
            if (parameter.Name.StartsWith("e_", StringComparison.Ordinal) && parameter.Name.Length > 2)
            {
                paths[parameter.Name] = parameter.Name[2..];
            }
        }

        return paths;
    }

    private static void CollectNecessaryPredicates(
        Expression expression,
        IReadOnlyDictionary<string, string> eventPaths,
        List<IndexedPredicate> predicates)
    {
        if (expression is BinaryExpression { Operator: "&&" } conjunction)
        {
            CollectNecessaryPredicates(conjunction.Left, eventPaths, predicates);
            CollectNecessaryPredicates(conjunction.Right, eventPaths, predicates);
            return;
        }

        if (TryPredicate(expression, eventPaths, out var predicate))
        {
            predicates.Add(predicate);
        }
    }

    private static void CollectNecessaryPredicates(
        IReadOnlyList<Statement> statements,
        IReadOnlyDictionary<string, string> eventPaths,
        List<IndexedPredicate> predicates)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case AssignmentStatement:
                    continue;
                case ReturnStatement returned:
                    CollectNecessaryPredicates(returned.Value, eventPaths, predicates);
                    return;
                case IfStatement branch when AlwaysReturnsFalse(branch.Else):
                    CollectNecessaryPredicates(branch.Condition, eventPaths, predicates);
                    CollectNecessaryPredicates(branch.Then, eventPaths, predicates);
                    return;
                default:
                    return;
            }
        }
    }

    private static bool AlwaysReturnsFalse(IReadOnlyList<Statement> statements)
        => statements.Count == 1 &&
           statements[0] is ReturnStatement
           {
               Value: LiteralExpression { Value: BoolValue { Value: false } }
           };

    private static bool TryPredicate(
        Expression expression,
        IReadOnlyDictionary<string, string> eventPaths,
        out IndexedPredicate predicate)
    {
        predicate = null!;
        if (expression is not BinaryExpression binary ||
            Operator(binary.Operator) is not { } op)
        {
            return TryCallPredicate(expression, eventPaths, out predicate);
        }

        if (TryVariableLiteral(binary.Left, binary.Right, eventPaths, op, out predicate))
        {
            return true;
        }

        return TryVariableLiteral(binary.Right, binary.Left, eventPaths, Flip(op), out predicate);
    }

    private static bool TryCallPredicate(
        Expression expression,
        IReadOnlyDictionary<string, string> eventPaths,
        out IndexedPredicate predicate)
    {
        predicate = null!;
        if (expression is UnaryExpression { Operator: "!", Operand: { } operand })
        {
            return TryStringEquals(operand, eventPaths, IndexPredicateOperator.NotEquals, out predicate);
        }

        return TryStringEquals(expression, eventPaths, IndexPredicateOperator.Equals, out predicate);
    }

    private static bool TryStringEquals(
        Expression expression,
        IReadOnlyDictionary<string, string> eventPaths,
        IndexPredicateOperator op,
        out IndexedPredicate predicate)
    {
        predicate = null!;
        if (expression is not CallExpression
            {
                Name: "string.equals",
                Arguments.Count: 2
            } call)
        {
            return false;
        }

        if (TryVariableLiteral(call.Arguments[0], call.Arguments[1], eventPaths, op, out predicate))
        {
            return true;
        }

        return TryVariableLiteral(call.Arguments[1], call.Arguments[0], eventPaths, op, out predicate);
    }

    private static bool TryVariableLiteral(
        Expression variable,
        Expression literal,
        IReadOnlyDictionary<string, string> eventPaths,
        IndexPredicateOperator op,
        out IndexedPredicate predicate)
    {
        predicate = null!;
        if (variable is not VariableExpression variableExpression ||
            !eventPaths.TryGetValue(variableExpression.Name, out var path) ||
            literal is not LiteralExpression literalExpression ||
            !TryLiteralValue(literalExpression.Value, out var value, out var valueType))
        {
            return false;
        }

        predicate = new IndexedPredicate(path, op, value, valueType);
        return true;
    }

    private static IndexPredicateOperator? Operator(string op)
        => op switch
        {
            "==" => IndexPredicateOperator.Equals,
            "!=" => IndexPredicateOperator.NotEquals,
            ">" => IndexPredicateOperator.GreaterThan,
            ">=" => IndexPredicateOperator.GreaterThanOrEqual,
            "<" => IndexPredicateOperator.LessThan,
            "<=" => IndexPredicateOperator.LessThanOrEqual,
            _ => null
        };

    private static IndexPredicateOperator Flip(IndexPredicateOperator op)
        => op switch
        {
            IndexPredicateOperator.GreaterThan => IndexPredicateOperator.LessThan,
            IndexPredicateOperator.GreaterThanOrEqual => IndexPredicateOperator.LessThanOrEqual,
            IndexPredicateOperator.LessThan => IndexPredicateOperator.GreaterThan,
            IndexPredicateOperator.LessThanOrEqual => IndexPredicateOperator.GreaterThanOrEqual,
            _ => op
        };

    private static bool TryLiteralValue(SandboxValue literal, out object value, out string valueType)
    {
        switch (literal)
        {
            case BoolValue boolValue:
                value = boolValue.Value;
                valueType = "bool";
                return true;
            case I32Value i32Value:
                value = i32Value.Value;
                valueType = "int";
                return true;
            case I64Value i64Value:
                value = i64Value.Value;
                valueType = "long";
                return true;
            case F64Value f64Value:
                value = f64Value.Value;
                valueType = "double";
                return true;
            case StringValue stringValue:
                value = stringValue.Value;
                valueType = "string";
                return true;
            default:
                value = "";
                valueType = "";
                return false;
        }
    }
}

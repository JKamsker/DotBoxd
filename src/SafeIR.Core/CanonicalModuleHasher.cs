namespace SafeIR;

public static class CanonicalModuleHasher
{
    public static string Hash(SandboxModule module)
        => CanonicalEncoding.HashRecords(new[] { Serialize(module) });

    public static string Serialize(SandboxModule module)
    {
        var writer = new CanonicalWriter();
        writer.Write("module", module.Id, module.Version.ToString(), module.TargetSandboxVersion.ToString());

        foreach (var request in module.CapabilityRequests.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            writer.Write("requires", request.Id, request.Reason ?? "");
        }

        foreach (var item in module.Metadata.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            writer.Write("metadata", item.Key, item.Value);
        }

        foreach (var function in module.Functions.OrderBy(f => f.Id, StringComparer.Ordinal))
        {
            WriteFunction(writer, function);
        }

        return writer.ToString();
    }

    private static void WriteFunction(CanonicalWriter writer, SandboxFunction function)
    {
        writer.Write("fn", function.IsEntrypoint ? "entry" : "private", function.Id, Type(function.ReturnType));
        foreach (var parameter in function.Parameters)
        {
            writer.Write("param", parameter.Name, Type(parameter.Type));
        }

        foreach (var statement in function.Body)
        {
            WriteStatement(writer, statement);
        }
    }

    private static void WriteStatement(CanonicalWriter writer, Statement statement)
    {
        switch (statement)
        {
            case AssignmentStatement assignment:
                writer.Write("set", assignment.Name, Expr(assignment.Value));
                break;
            case ReturnStatement ret:
                writer.Write("return", Expr(ret.Value));
                break;
            case ExpressionStatement expr:
                writer.Write("expr", Expr(expr.Value));
                break;
            case IfStatement branch:
                writer.Write("if", Expr(branch.Condition));
                branch.Then.ToList().ForEach(s => WriteStatement(writer, s));
                writer.Write("else");
                branch.Else.ToList().ForEach(s => WriteStatement(writer, s));
                writer.Write("endif");
                break;
            case WhileStatement loop:
                writer.Write("while", Expr(loop.Condition));
                loop.Body.ToList().ForEach(s => WriteStatement(writer, s));
                writer.Write("endwhile");
                break;
            case ForRangeStatement range:
                writer.Write("for", range.LocalName, Expr(range.Start), Expr(range.End));
                range.Body.ToList().ForEach(s => WriteStatement(writer, s));
                writer.Write("endfor");
                break;
            default:
                throw new NotSupportedException(statement.GetType().Name);
        }
    }

    private static string Expr(Expression expression) => expression switch
    {
        LiteralExpression literal => Node("lit", Value(literal.Value)),
        VariableExpression variable => Node("var", variable.Name),
        UnaryExpression unary => Node("unary", unary.Operator, Expr(unary.Operand)),
        BinaryExpression binary => Node("bin", binary.Operator, Expr(binary.Left), Expr(binary.Right)),
        CallExpression call => Call(call),
        _ => throw new NotSupportedException(expression.GetType().Name)
    };

    private static string Value(SandboxValue value)
        => value switch
        {
            UnitValue => Node("unit"),
            BoolValue boolean => Node("bool", boolean.Value ? "true" : "false"),
            I32Value number => Node("i32", number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            I64Value number => Node("i64", number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            F64Value number => Node("f64", number.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)),
            StringValue text => Node("string", text.Value),
            OpaqueIdValue id => Node("opaque-id", id.TypeName, id.Value),
            SandboxPathValue path => Node("path", path.Value.RelativePath),
            SandboxUriValue uri => Node("uri", uri.Value.Value),
            ListValue list => ListLiteral(list),
            MapValue map => MapLiteral(map),
            _ => throw new NotSupportedException(value.GetType().Name)
        };

    private static string Type(SandboxType type)
    {
        var fields = new List<string?> { "type", type.Name };
        fields.AddRange(type.Arguments.Select(Type));
        return Node(fields);
    }

    private static string Call(CallExpression call)
    {
        var fields = new List<string?> {
            "call",
            call.Name,
            call.GenericType is null ? null : Type(call.GenericType)
        };
        fields.AddRange(call.Arguments.Select(Expr));
        return Node(fields);
    }

    private static string ListLiteral(ListValue list)
    {
        var fields = new List<string?> { "list", Type(list.ItemType) };
        fields.AddRange(list.Values.Select(Value));
        return Node(fields);
    }

    private static string MapLiteral(MapValue map)
    {
        var entries = map.Values
            .Select(p => Node("entry", Value(p.Key), Value(p.Value)))
            .Order(StringComparer.Ordinal);
        var fields = new List<string?> { "map", Type(map.KeyType), Type(map.ValueType) };
        fields.AddRange(entries);
        return Node(fields);
    }

    private static string Node(params string?[] fields)
        => CanonicalEncoding.Record(fields);

    private static string Node(IEnumerable<string?> fields)
        => CanonicalEncoding.Record(fields);

    private sealed class CanonicalWriter
    {
        private readonly System.Text.StringBuilder _builder = new();

        public void Write(params string[] fields)
        {
            _builder.Append(CanonicalEncoding.Record(fields));
            _builder.Append('\n');
        }

        public override string ToString() => _builder.ToString();
    }
}

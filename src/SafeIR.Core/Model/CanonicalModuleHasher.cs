namespace SafeIR;

public static class CanonicalModuleHasher
{
    public const string CanonicalizerVersion = "safe-ir-canonicalizer-1";

    public static string Hash(SandboxModule module)
        => CanonicalEncoding.HashRecord(Serialize(module));

    public static string Serialize(SandboxModule module)
    {
        var writer = new CanonicalWriter();
        writer.Write("canonicalizer", CanonicalizerVersion);
        writer.Write("module", module.Id, module.Version.ToString(), module.TargetSandboxVersion.ToString());

        WriteCapabilityRequests(writer, module.CapabilityRequests);
        WriteMetadata(writer, module.Metadata);
        WriteFunctions(writer, module.Functions);

        return writer.ToString();
    }

    private static void WriteCapabilityRequests(
        CanonicalWriter writer,
        IReadOnlyList<CapabilityRequest> requests)
    {
        if (requests.Count == 0)
        {
            return;
        }

        var ordered = new CapabilityRequest[requests.Count];
        for (var i = 0; i < requests.Count; i++)
        {
            ordered[i] = requests[i];
        }

        Array.Sort(ordered, static (left, right) =>
        {
            var idComparison = string.Compare(left.Id, right.Id, StringComparison.Ordinal);
            return idComparison != 0
                ? idComparison
                : string.Compare(left.Reason ?? "", right.Reason ?? "", StringComparison.Ordinal);
        });
        for (var i = 0; i < ordered.Length; i++)
        {
            writer.Write("requires", ordered[i].Id, ordered[i].Reason ?? "");
        }
    }

    private static void WriteMetadata(
        CanonicalWriter writer,
        IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.Count == 0)
        {
            return;
        }

        var ordered = new KeyValuePair<string, string>[metadata.Count];
        var index = 0;
        foreach (var item in metadata)
        {
            ordered[index++] = item;
        }

        Array.Sort(ordered, static (left, right) => string.Compare(left.Key, right.Key, StringComparison.Ordinal));
        for (var i = 0; i < ordered.Length; i++)
        {
            writer.Write("metadata", ordered[i].Key, ordered[i].Value);
        }
    }

    private static void WriteFunctions(
        CanonicalWriter writer,
        IReadOnlyList<SandboxFunction> functions)
    {
        if (functions.Count == 0)
        {
            return;
        }

        var ordered = new SandboxFunction[functions.Count];
        for (var i = 0; i < functions.Count; i++)
        {
            ordered[i] = functions[i];
        }

        Array.Sort(ordered, static (left, right) => string.Compare(left.Id, right.Id, StringComparison.Ordinal));
        for (var i = 0; i < ordered.Length; i++)
        {
            WriteFunction(writer, ordered[i]);
        }
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
                WriteStatements(writer, branch.Then);
                writer.Write("else");
                WriteStatements(writer, branch.Else);
                writer.Write("endif");
                break;
            case WhileStatement loop:
                writer.Write("while", Expr(loop.Condition));
                WriteStatements(writer, loop.Body);
                writer.Write("endwhile");
                break;
            case ForRangeStatement range:
                writer.Write("for", range.LocalName, Expr(range.Start), Expr(range.End));
                WriteStatements(writer, range.Body);
                writer.Write("endfor");
                break;
            default:
                throw new NotSupportedException(statement.GetType().Name);
        }
    }

    private static void WriteStatements(CanonicalWriter writer, IReadOnlyList<Statement> statements)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            WriteStatement(writer, statements[i]);
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
        var fields = new List<string?>(2 + type.Arguments.Count) { "type", type.Name };
        for (var i = 0; i < type.Arguments.Count; i++)
        {
            fields.Add(Type(type.Arguments[i]));
        }

        return Node(fields);
    }

    private static string Call(CallExpression call)
    {
        var fields = new List<string?>(3 + call.Arguments.Count) {
            "call",
            call.Name,
            call.GenericType is null ? null : Type(call.GenericType)
        };
        for (var i = 0; i < call.Arguments.Count; i++)
        {
            fields.Add(Expr(call.Arguments[i]));
        }

        return Node(fields);
    }

    private static string ListLiteral(ListValue list)
    {
        var fields = new List<string?>(2 + list.Values.Count) { "list", Type(list.ItemType) };
        for (var i = 0; i < list.Values.Count; i++)
        {
            fields.Add(Value(list.Values[i]));
        }

        return Node(fields);
    }

    private static string MapLiteral(MapValue map)
    {
        var entries = map.Values.Count == 0 ? Array.Empty<string>() : new string[map.Values.Count];
        var index = 0;
        foreach (var item in map.Values)
        {
            entries[index++] = Node("entry", Value(item.Key), Value(item.Value));
        }

        Array.Sort(entries, StringComparer.Ordinal);
        var fields = new List<string?>(3 + entries.Length) { "map", Type(map.KeyType), Type(map.ValueType) };
        for (var i = 0; i < entries.Length; i++)
        {
            fields.Add(entries[i]);
        }

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

using System.Security.Cryptography;
using System.Text;

namespace SafeIR;

public static class CanonicalModuleHasher
{
    public static string Hash(SandboxModule module)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(module)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Serialize(SandboxModule module)
    {
        var writer = new CanonicalWriter();
        writer.Write("module", module.Id, module.Version.ToString(), module.TargetSandboxVersion.ToString());

        foreach (var request in module.CapabilityRequests.OrderBy(r => r.Id, StringComparer.Ordinal)) {
            writer.Write("requires", request.Id, request.Reason ?? "");
        }

        foreach (var item in module.Metadata.OrderBy(m => m.Key, StringComparer.Ordinal)) {
            writer.Write("metadata", item.Key, item.Value);
        }

        foreach (var function in module.Functions.OrderBy(f => f.Id, StringComparer.Ordinal)) {
            WriteFunction(writer, function);
        }

        return writer.ToString();
    }

    private static void WriteFunction(CanonicalWriter writer, SandboxFunction function)
    {
        writer.Write("fn", function.IsEntrypoint ? "entry" : "private", function.Id, function.ReturnType.ToString());
        foreach (var parameter in function.Parameters) {
            writer.Write("param", parameter.Name, parameter.Type.ToString());
        }

        foreach (var statement in function.Body) {
            WriteStatement(writer, statement);
        }
    }

    private static void WriteStatement(CanonicalWriter writer, Statement statement)
    {
        switch (statement) {
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
        }
    }

    private static string Expr(Expression expression) => expression switch {
        LiteralExpression literal => "lit(" + Value(literal.Value) + ")",
        VariableExpression variable => $"var({variable.Name})",
        UnaryExpression unary => $"unary({unary.Operator},{Expr(unary.Operand)})",
        BinaryExpression binary => $"bin({binary.Operator},{Expr(binary.Left)},{Expr(binary.Right)})",
        CallExpression call => $"call({call.Name},{call.GenericType},{string.Join(",", call.Arguments.Select(Expr))})",
        _ => throw new NotSupportedException(expression.GetType().Name)
    };

    private static string Value(SandboxValue value)
        => value switch {
            UnitValue => "unit",
            BoolValue boolean => "bool:" + (boolean.Value ? "true" : "false"),
            I32Value number => "i32:" + number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            I64Value number => "i64:" + number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            F64Value number => "f64:" + number.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            StringValue text => "string:" + text.Value,
            SandboxPathValue path => "path:" + path.Value.RelativePath,
            SandboxUriValue uri => "uri:" + uri.Value.Value,
            ListValue list => "list:" + list.ItemType + "[" + string.Join(",", list.Values.Select(Value)) + "]",
            MapValue map => "map:" + map.KeyType + ":" + map.ValueType + "{" +
                            string.Join(",", map.Values.OrderBy(p => Value(p.Key), StringComparer.Ordinal)
                                .Select(p => Value(p.Key) + "=>" + Value(p.Value))) + "}",
            _ => throw new NotSupportedException(value.GetType().Name)
        };

    private sealed class CanonicalWriter
    {
        private readonly StringBuilder _builder = new();

        public void Write(params string[] fields)
        {
            _builder.Append(string.Join('\u001f', fields.Select(Escape)));
            _builder.Append('\n');
        }

        public override string ToString() => _builder.ToString();

        private static string Escape(string value)
            => value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\u001f", "\\u001f", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}

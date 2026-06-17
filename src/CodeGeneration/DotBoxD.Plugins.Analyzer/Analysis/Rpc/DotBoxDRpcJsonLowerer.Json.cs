using System.Globalization;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private static string LiteralJson(object? value)
        => value switch
        {
            bool b => Obj(("bool", b ? "true" : "false")),
            int i => Obj(("i32", i.ToString(CultureInfo.InvariantCulture))),
            long l => Obj(("i64", l.ToString(CultureInfo.InvariantCulture))),
            double d => FiniteDoubleLiteralJson(d),
            string s => Obj(("string", Str(s))),
            _ => throw new NotSupportedException($"Kernel RPC service literal '{value}' is not supported.")
        };

    private static string FiniteDoubleLiteralJson(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new NotSupportedException("Kernel RPC service F64 literals must be finite.");
        }

        return Obj(("f64", value.ToString("R", CultureInfo.InvariantCulture)));
    }

    internal static string Var(string name) => Obj(("var", Str(name)));

    private static string I32(int value) => Obj(("i32", value.ToString(CultureInfo.InvariantCulture)));

    private static string BinaryJson(string op, string left, string right)
        => Obj(("op", Str(op)), ("left", left), ("right", right));

    private static string Call(string name, string? genericType, params string[] args)
    {
        var fields = new List<(string, string)>(3) { ("call", Str(name)) };
        if (genericType is not null)
        {
            fields.Add(("genericType", genericType));
        }

        fields.Add(("args", "[" + string.Join(",", args) + "]"));
        return Obj(fields.ToArray());
    }

    private static string SetStatement(string name, string value)
        => Obj(("op", Str("set")), ("name", Str(name)), ("value", value));

    private static string Obj(params (string Key, string Value)[] fields)
    {
        var parts = new string[fields.Length];
        for (var i = 0; i < fields.Length; i++)
        {
            parts[i] = Str(fields[i].Key) + ":" + fields[i].Value;
        }

        return "{" + string.Join(",", parts) + "}";
    }

    internal static string Str(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (ch < ' ')
                    {
                        builder.Append("\\u")
                            .Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        break;
                    }

                    builder.Append(ch);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}

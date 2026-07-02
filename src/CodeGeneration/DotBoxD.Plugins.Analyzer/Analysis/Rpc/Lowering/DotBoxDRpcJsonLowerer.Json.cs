using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private static string LiteralJson(object? value)
        => value switch
        {
            bool b => Obj(("bool", b ? "true" : "false")),
            int i => Obj(("i32", i.ToString(CultureInfo.InvariantCulture))),
            long l => Obj(("i64", l.ToString(CultureInfo.InvariantCulture))),
            float f => FiniteDoubleLiteralJson(f),
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

    private static string JsonBinaryOperator(BinaryExpressionSyntax binary)
        => binary.Kind() switch
        {
            SyntaxKind.AddExpression => "add",
            SyntaxKind.SubtractExpression => "sub",
            SyntaxKind.MultiplyExpression => "mul",
            SyntaxKind.DivideExpression => "div",
            SyntaxKind.ModuloExpression => "rem",
            SyntaxKind.EqualsExpression => "eq",
            SyntaxKind.NotEqualsExpression => "ne",
            SyntaxKind.LessThanExpression => "lt",
            SyntaxKind.LessThanOrEqualExpression => "lte",
            SyntaxKind.GreaterThanExpression => "gt",
            SyntaxKind.GreaterThanOrEqualExpression => "gte",
            SyntaxKind.LogicalAndExpression => "and",
            SyntaxKind.LogicalOrExpression => "or",
            _ => throw new NotSupportedException($"Server extension operator '{binary.OperatorToken.ValueText}' is not supported.")
        };

    internal static string RecordGet(string receiver, int index)
        => Call("record.get", null, receiver, I32(index));

    private static string Unit() => Obj(("unit", "true"));

    private static string I32(int value) => Obj(("i32", value.ToString(CultureInfo.InvariantCulture)));

    private static string I64(long value) => Obj(("i64", value.ToString(CultureInfo.InvariantCulture)));

    private static string DecimalLiteralJson(decimal value)
    {
        var bits = decimal.GetBits(value);
        return Call(
            "record.new",
            DotBoxDRpcTypeMapper.DecimalWireJsonType(),
            I32(bits[0]),
            I32(bits[1]),
            I32(bits[2]),
            I32(bits[3]));
    }

    private static string EnumLiteralJson(INamedTypeSymbol enumType, object? value)
    {
        if (value is null)
        {
            throw new NotSupportedException(
                $"Kernel RPC service enum literal '{enumType.ToDisplayString()}' is not supported.");
        }

        return DotBoxDRpcTypeMapper.EnumUsesI64(enumType)
            ? I64(EnumInt64Value(enumType, value))
            : I32(unchecked((int)EnumInt64Value(enumType, value)));
    }

    private static long EnumInt64Value(INamedTypeSymbol enumType, object value)
        => enumType.EnumUnderlyingType?.SpecialType switch
        {
            SpecialType.System_UInt64 => unchecked((long)(ulong)value),
            SpecialType.System_UInt32 => (uint)value,
            SpecialType.System_Int64 => (long)value,
            SpecialType.System_Int32 => (int)value,
            SpecialType.System_UInt16 => (ushort)value,
            SpecialType.System_Int16 => (short)value,
            SpecialType.System_Byte => (byte)value,
            SpecialType.System_SByte => (sbyte)value,
            _ => throw new NotSupportedException(
                $"Kernel RPC service enum literal '{enumType.ToDisplayString()}' is not supported.")
        };

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
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
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

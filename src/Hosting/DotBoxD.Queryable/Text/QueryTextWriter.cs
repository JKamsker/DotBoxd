using System.Globalization;
using System.Text;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Text;

/// <summary>Formats a <see cref="QueryFilter"/> into the portable text DSL (see <see cref="QueryText"/>).</summary>
internal static class QueryTextWriter
{
    public static string Write(QueryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        QueryFilterInvariants.RequireValidShape(filter);
        var builder = new StringBuilder();
        Write(filter, builder);
        return builder.ToString();
    }

    private static void Write(QueryFilter filter, StringBuilder builder)
    {
        switch (filter.Kind)
        {
            case QueryFilterKind.MatchAll:
                builder.Append('*');
                break;
            case QueryFilterKind.And:
                WriteConnective(filter.Children, "and", builder);
                break;
            case QueryFilterKind.Or:
                WriteConnective(filter.Children, "or", builder);
                break;
            case QueryFilterKind.Not:
                builder.Append("not ");
                Write(filter.Children[0], builder);
                break;
            case QueryFilterKind.Compare:
                WriteLeaf(filter, builder, QueryText.OperatorToken(filter.Operator), value => Write(value, builder));
                break;
            case QueryFilterKind.In:
                WriteIn(filter, builder);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(filter), filter.Kind, "Unknown filter kind.");
        }
    }

    private static void WriteConnective(IReadOnlyList<QueryFilter> children, string keyword, StringBuilder builder)
    {
        builder.Append('(');
        for (var i = 0; i < children.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(' ').Append(keyword).Append(' ');
            }

            Write(children[i], builder);
        }

        builder.Append(')');
    }

    private static void WriteLeaf(QueryFilter filter, StringBuilder builder, string op, Action<QueryValue> writeValue)
    {
        builder.Append(filter.Field).Append(' ');
        if (filter.IgnoreCase)
        {
            builder.Append('~');
        }

        builder.Append(op).Append(' ');
        writeValue(QueryFilterInvariants.CompareValue(filter));
    }

    private static void WriteIn(QueryFilter filter, StringBuilder builder)
    {
        builder.Append(filter.Field).Append(' ');
        if (filter.IgnoreCase)
        {
            builder.Append('~');
        }

        builder.Append("in [");
        for (var i = 0; i < filter.Values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            Write(filter.Values[i], builder);
        }

        builder.Append(']');
    }

    private static void Write(QueryValue value, StringBuilder builder)
    {
        switch (value.Kind)
        {
            case QueryValueKind.Null:
                builder.Append("null");
                break;
            case QueryValueKind.Boolean:
                builder.Append(value.Boolean ? "true" : "false");
                break;
            case QueryValueKind.Integer:
                builder.Append(value.Integer.ToString(CultureInfo.InvariantCulture));
                break;
            case QueryValueKind.Number:
                builder.Append(value.Number.ToString("R", CultureInfo.InvariantCulture));
                break;
            case QueryValueKind.String:
                WriteString(value.String ?? string.Empty, builder);
                break;
            case QueryValueKind.Decimal:
                builder.Append(QueryValue.CanonicalDecimal(value.Decimal)).Append('m');
                break;
            case QueryValueKind.UnsignedInteger:
                builder.Append(value.UnsignedInteger.ToString(CultureInfo.InvariantCulture)).Append('u');
                break;
            case QueryValueKind.Guid:
                builder.Append("guid(\"").Append(value.Guid.ToString("D")).Append("\")");
                break;
            case QueryValueKind.Timestamp:
                builder.Append("ts(\"").Append(QueryValue.CanonicalTimestamp(value.Timestamp)).Append("\")");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "Unknown value kind.");
        }
    }

    private static void WriteString(string value, StringBuilder builder)
    {
        builder.Append('"');
        foreach (var ch in value)
        {
            if (ch is '"' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(ch);
        }

        builder.Append('"');
    }
}

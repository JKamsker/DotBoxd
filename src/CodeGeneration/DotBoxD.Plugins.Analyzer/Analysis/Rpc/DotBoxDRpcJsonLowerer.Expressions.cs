using System.Globalization;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

/// <summary>
/// Expression lowering and JSON emission for <see cref="DotBoxDRpcJsonLowerer"/>: constants, identifiers,
/// operators, host-binding calls, <c>list.*</c>/<c>record.*</c> intrinsics, DTO construction, and the
/// small JSON writer the statement half also uses.
/// </summary>
internal sealed partial class DotBoxDRpcJsonLowerer
{
    public string LowerExpression(ExpressionSyntax expression)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (_model.GetConstantValue(expression, _cancellationToken) is { HasValue: true } constant)
        {
            return LiteralJson(constant.Value);
        }

        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return LowerExpression(parenthesized.Expression);
            case IdentifierNameSyntax identifier:
                return Var(identifier.Identifier.ValueText);
            case PrefixUnaryExpressionSyntax unary:
                return LowerUnary(unary);
            case BinaryExpressionSyntax binary:
                return BinaryJson(JsonBinaryOperator(binary), LowerExpression(binary.Left), LowerExpression(binary.Right));
            case InvocationExpressionSyntax invocation:
                return LowerInvocation(invocation);
            case ObjectCreationExpressionSyntax creation:
                return LowerRecordCreation(creation);
            case ElementAccessExpressionSyntax element:
                return LowerElementAccess(element);
            case MemberAccessExpressionSyntax member:
                return LowerMemberAccess(member);
            default:
                throw new NotSupportedException($"Server extension expression '{expression}' is not supported.");
        }
    }

    private string LowerUnary(PrefixUnaryExpressionSyntax unary)
        => unary.Kind() switch
        {
            SyntaxKind.LogicalNotExpression => Obj(("op", Str("not")), ("operand", LowerExpression(unary.Operand))),
            SyntaxKind.UnaryMinusExpression => Obj(("op", Str("-")), ("operand", LowerExpression(unary.Operand))),
            _ => throw new NotSupportedException($"Server extension unary '{unary.Kind()}' is not supported.")
        };

    private string LowerInvocation(InvocationExpressionSyntax invocation)
    {
        if (_model.GetSymbolInfo(invocation, _cancellationToken).Symbol is IMethodSymbol method &&
            DotBoxDHostBindingExpressionLowerer.HostBinding(method) is { } binding)
        {
            _capabilities.Add(binding.Capability);
            foreach (var effect in binding.Effects)
            {
                _effects.Add(effect);
            }

            var args = new List<string>();
            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                args.Add(LowerExpression(argument.Expression));
            }

            return Call(binding.BindingId, null, args.ToArray());
        }

        throw new NotSupportedException($"Server extension call '{invocation}' is not a host binding.");
    }

    private string LowerMemberAccess(MemberAccessExpressionSyntax member)
    {
        var receiverType = TypeOf(member.Expression);
        if (string.Equals(member.Name.Identifier.ValueText, "Count", StringComparison.Ordinal) &&
            DotBoxDRpcTypeMapper.ListElementType(receiverType) is not null)
        {
            return Call("list.count", null, LowerExpression(member.Expression));
        }

        if (receiverType is INamedTypeSymbol named && DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            var fields = DotBoxDRpcTypeMapper.RecordFields(named);
            for (var i = 0; i < fields.Count; i++)
            {
                if (string.Equals(fields[i].Name, member.Name.Identifier.ValueText, StringComparison.Ordinal))
                {
                    return Call("record.get", null, LowerExpression(member.Expression), I32(i));
                }
            }
        }

        throw new NotSupportedException($"Server extension member access '{member}' is not supported.");
    }

    private string LowerElementAccess(ElementAccessExpressionSyntax element)
    {
        if (element.ArgumentList.Arguments.Count != 1 ||
            DotBoxDRpcTypeMapper.ListElementType(TypeOf(element.Expression)) is null)
        {
            throw new NotSupportedException($"Server extension indexing '{element}' is not supported.");
        }

        return Call("list.get", null, LowerExpression(element.Expression), LowerExpression(element.ArgumentList.Arguments[0].Expression));
    }

    private string LowerRecordCreation(ObjectCreationExpressionSyntax creation)
    {
        var created = TypeOf(creation);
        // new List<T>() (or other empty collection) → an empty typed list.
        if (DotBoxDRpcTypeMapper.ListElementType(created) is { } elementType &&
            creation.Initializer is null &&
            (creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0))
        {
            Allocates = true;
            return Call("list.empty", DotBoxDRpcTypeMapper.JsonType(elementType));
        }

        if (created is not INamedTypeSymbol named || !DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            throw new NotSupportedException($"Server extension 'new {creation.Type}' must construct a supported DTO or empty list.");
        }

        Allocates = true;
        var fields = DotBoxDRpcTypeMapper.RecordFields(named);
        var args = new string[fields.Count];
        if (creation.ArgumentList is { Arguments.Count: > 0 } argumentList)
        {
            if (argumentList.Arguments.Count != fields.Count)
            {
                throw new NotSupportedException($"Server extension constructor for '{named.Name}' must pass one argument per field.");
            }

            for (var i = 0; i < fields.Count; i++)
            {
                args[i] = LowerExpression(argumentList.Arguments[i].Expression);
            }
        }
        else if (creation.Initializer is { } initializer)
        {
            BindInitializer(initializer, fields, named, args);
        }
        else
        {
            throw new NotSupportedException($"Server extension 'new {named.Name}' must use constructor arguments or an object initializer.");
        }

        return Call("record.new", DotBoxDRpcTypeMapper.JsonType(named), args);
    }

    private void BindInitializer(
        InitializerExpressionSyntax initializer,
        IReadOnlyList<IPropertySymbol> fields,
        INamedTypeSymbol named,
        string[] args)
    {
        var assigned = new bool[fields.Count];
        foreach (var entry in initializer.Expressions)
        {
            if (entry is not AssignmentExpressionSyntax { Left: IdentifierNameSyntax fieldName } assignment)
            {
                throw new NotSupportedException($"Server extension initializer for '{named.Name}' must assign named fields.");
            }

            var index = IndexOfField(fields, fieldName.Identifier.ValueText, named);
            args[index] = LowerExpression(assignment.Right);
            assigned[index] = true;
        }

        for (var i = 0; i < assigned.Length; i++)
        {
            if (!assigned[i])
            {
                throw new NotSupportedException($"Server extension initializer for '{named.Name}' must set field '{fields[i].Name}'.");
            }
        }
    }

    private static int IndexOfField(IReadOnlyList<IPropertySymbol> fields, string name, INamedTypeSymbol named)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new NotSupportedException($"Server extension '{named.Name}' has no field '{name}'.");
    }

    private ITypeSymbol TypeOf(ExpressionSyntax expression)
        => _model.GetTypeInfo(expression, _cancellationToken).Type
           ?? throw new NotSupportedException($"Server extension could not resolve the type of '{expression}'.");

    private string JsonBinaryOperator(BinaryExpressionSyntax binary)
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

    private static string LiteralJson(object? value)
        => value switch
        {
            bool b => Obj(("bool", b ? "true" : "false")),
            int i => Obj(("i32", i.ToString(CultureInfo.InvariantCulture))),
            long l => Obj(("i64", l.ToString(CultureInfo.InvariantCulture))),
            double d => Obj(("f64", d.ToString("R", CultureInfo.InvariantCulture))),
            string s => Obj(("string", Str(s))),
            _ => throw new NotSupportedException($"Server extension literal '{value}' is not supported.")
        };

    private static string Var(string name) => Obj(("var", Str(name)));

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
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}

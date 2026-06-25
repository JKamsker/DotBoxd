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
            if (constant.Value is string)
            {
                Allocates = true;
            }
            return LiteralJson(expression, constant.Value);
        }
        var lowered = expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => LowerExpression(parenthesized.Expression),
            AwaitExpressionSyntax awaited => LowerExpression(awaited.Expression),
            IdentifierNameSyntax identifier => LowerIdentifier(identifier),
            PrefixUnaryExpressionSyntax unary => LowerUnary(unary),
            BinaryExpressionSyntax binary => BinaryJson(
                JsonBinaryOperator(binary),
                LowerExpression(binary.Left),
                LowerExpression(binary.Right)),
            InvocationExpressionSyntax invocation => LowerInvocation(invocation),
            ObjectCreationExpressionSyntax creation => LowerRecordCreation(creation),
            ElementAccessExpressionSyntax element => LowerElementAccess(element),
            MemberAccessExpressionSyntax member => LowerMemberAccess(member),
            _ => throw new NotSupportedException($"Server extension expression '{expression}' is not supported.")
        };
        return ApplyNumericConversion(expression, lowered);
    }
    private string LowerUnary(PrefixUnaryExpressionSyntax unary)
        => unary.Kind() switch
        {
            SyntaxKind.LogicalNotExpression => Obj(("unary", Str("not")), ("operand", LowerExpression(unary.Operand))),
            SyntaxKind.UnaryMinusExpression => Obj(("unary", Str("-")), ("operand", LowerExpression(unary.Operand))),
            _ => throw new NotSupportedException($"Server extension unary '{unary.Kind()}' is not supported.")
        };
    private string LiteralJson(ExpressionSyntax expression, object? value)
    {
        var converted = _model.GetTypeInfo(expression, _cancellationToken).ConvertedType;
        if (converted?.SpecialType == SpecialType.System_Int64 && value is int i)
        {
            return LiteralJson((long)i);
        }
        if (converted?.SpecialType == SpecialType.System_Double && value is IConvertible convertible)
        {
            return LiteralJson(convertible.ToDouble(CultureInfo.InvariantCulture));
        }
        return LiteralJson(value);
    }
    private string LowerInvocation(InvocationExpressionSyntax invocation)
    {
        if (TryLowerServiceHandleInvocation(invocation) is { } serviceHandleCall)
        {
            return serviceHandleCall;
        }
        if (TryLowerMapMethod(invocation) is { } mapCall)
        {
            return mapCall;
        }

        if (_model.GetSymbolInfo(invocation, _cancellationToken).Symbol is IMethodSymbol method &&
            DotBoxDHostBindingExpressionLowerer.HostBinding(method, _model.Compilation) is { } binding)
        {
            AddBindingMetadata(binding);
            var args = LowerArgumentsInParameterOrder(
                invocation.ArgumentList.Arguments,
                method.Parameters,
                $"Host binding '{binding.BindingId}'");
            return Call(binding.BindingId, null, args);
        }
        if (TryLowerKernelMethodInvocation(invocation) is { } kernelMethod)
        {
            return kernelMethod;
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
        var receiverType = TypeOf(element.Expression);
        if (element.ArgumentList.Arguments.Count == 1 &&
            DotBoxDRpcTypeMapper.ListElementType(receiverType) is not null)
        {
            return Call("list.get", null, LowerExpression(element.Expression), LowerExpression(element.ArgumentList.Arguments[0].Expression));
        }
        return TryLowerMapElementGet(element, receiverType)
            ?? throw new NotSupportedException($"Server extension indexing '{element}' is not supported.");
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
        if (TryLowerEmptyMapCreation(creation, created) is { } emptyMap)
        {
            return emptyMap;
        }
        if (created is not INamedTypeSymbol named || !DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            throw new NotSupportedException($"Server extension 'new {creation.Type}' must construct a supported DTO or empty list.");
        }
        Allocates = true;
        var fields = DotBoxDRpcTypeMapper.RecordFields(named);
        var args = new string[fields.Count];
        if (creation.ArgumentList is { Arguments.Count: > 0 } && creation.Initializer is not null)
        {
            throw new NotSupportedException(
                $"Server extension 'new {named.Name}' cannot combine constructor arguments and object initializers.");
        }
        if (creation.ArgumentList is { Arguments.Count: > 0 } argumentList)
        {
            // A record's constructor sets only its declared members; derived/get-only members (e.g. `Sum => X + Y`)
            // still appear as wire fields but have no constructor parameter, so the parameter count is a subset of
            // the field count. (Mirrors the runtime KernelRpcMarshaller.FindConstructor.)
            if (_model.GetSymbolInfo(creation, _cancellationToken).Symbol is not IMethodSymbol constructor ||
                argumentList.Arguments.Count != constructor.Parameters.Length ||
                constructor.Parameters.Length > fields.Count)
            {
                throw new NotSupportedException($"Server extension constructor for '{named.Name}' must pass one argument per constructor parameter, and the constructor must not have more parameters than the record has fields.");
            }
            var lowered = LowerArgumentsInParameterOrder(
                argumentList.Arguments,
                constructor.Parameters,
                $"Server extension constructor for '{named.Name}'");
            var assigned = new bool[fields.Count];
            for (var i = 0; i < constructor.Parameters.Length; i++)
            {
                var fieldIndex = ConstructorFieldIndex(fields, constructor.Parameters[i], named);
                if (assigned[fieldIndex])
                {
                    throw new NotSupportedException(
                        $"Server extension constructor for '{named.Name}' must map one argument per field.");
                }
                args[fieldIndex] = lowered[i];
                assigned[fieldIndex] = true;
            }
            // Each remaining field has no constructor parameter — it is a derived/get-only member. Build its wire
            // slot by lowering its getter over the constructor-bound members, so the constructed record carries
            // the same derived value the member would compute (and an in-sandbox read of it stays correct).
            for (var i = 0; i < fields.Count; i++)
            {
                if (!assigned[i])
                {
                    args[i] = LowerDerivedField(fields, assigned, args, named, fields[i]);
                }
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
        IReadOnlyList<RecordMember> fields,
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
    private static int IndexOfField(IReadOnlyList<RecordMember> fields, string name, INamedTypeSymbol named)
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

    private static int ConstructorFieldIndex(
        IReadOnlyList<RecordMember> fields,
        IParameterSymbol parameter,
        INamedTypeSymbol named)
    {
        var index = RpcDtoFieldMatcher.FieldIndex(fields, parameter);
        if (index >= 0)
        {
            return index;
        }

        throw new NotSupportedException(
            $"Server extension DTO '{named.Name}' must expose a constructor matching its public fields.");
    }

    private ITypeSymbol TypeOf(ExpressionSyntax expression)
        => _model.GetTypeInfo(expression, _cancellationToken).Type
           ?? throw new NotSupportedException($"Server extension could not resolve the type of '{expression}'.");

    private static bool HasDotBoxDServiceAttribute(ITypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(
                attribute.AttributeClass?.ToDisplayString(),
                "DotBoxD.Services.Attributes.DotBoxDServiceAttribute",
                StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

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
}

using System.Globalization;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
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
        if (_expressionOverride?.Invoke(expression) is { } overridden)
        {
            return ApplyNumericConversion(expression, overridden);
        }

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
            CastExpressionSyntax cast => LowerCast(cast),
            BinaryExpressionSyntax binary => LowerBinary(binary),
            InvocationExpressionSyntax invocation => LowerInvocation(invocation),
            ObjectCreationExpressionSyntax creation => LowerRecordCreation(creation),
            ImplicitObjectCreationExpressionSyntax creation => LowerRecordCreation(creation),
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
            SyntaxKind.UnaryPlusExpression => LowerExpression(unary.Operand),
            _ => throw new NotSupportedException($"Server extension unary '{unary.Kind()}' is not supported.")
        };

    private string LowerBinary(BinaryExpressionSyntax binary)
        => LowerBinary(binary, LowerExpression);

    private string LowerBinary(BinaryExpressionSyntax binary, Func<ExpressionSyntax, string> lower)
    {
        if (binary.Kind() == SyntaxKind.AddExpression)
        {
            var leftIsString = IsStringExpression(binary.Left);
            var rightIsString = IsStringExpression(binary.Right);
            if (leftIsString && rightIsString)
            {
                Allocates = true;
                return Call("string.concatBudgeted", null, lower(binary.Left), lower(binary.Right));
            }

            if (leftIsString || rightIsString)
            {
                throw new NotSupportedException(
                    "Server extension operator '+' requires both operands to be strings or matching numeric operands.");
            }
        }

        return BinaryJson(JsonBinaryOperator(binary), lower(binary.Left), lower(binary.Right));
    }

    private string LiteralJson(ExpressionSyntax expression, object? value)
    {
        var converted = _model.GetTypeInfo(expression, _cancellationToken).ConvertedType;
        if (converted is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            return EnumLiteralJson(enumType, value);
        }

        if (converted?.SpecialType == SpecialType.System_Int64 && value is int i)
        {
            return LiteralJson((long)i);
        }
        if (converted?.SpecialType is SpecialType.System_Double or SpecialType.System_Single &&
            value is IConvertible convertible)
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

        var symbolInfo = _model.GetSymbolInfo(invocation, _cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method &&
            DotBoxDHostBindingExpressionLowerer.HostBinding(method, _model.Compilation) is { } binding)
        {
            AddBindingMetadata(binding);
            var args = LowerArgumentsInParameterOrder(
                invocation.ArgumentList.Arguments,
                method.Parameters,
                $"Host binding '{binding.BindingId}'");
            return Call(binding.BindingId, null, args);
        }
        if (TryLowerServerContextHostBinding(invocation, symbolInfo.Symbol as IMethodSymbol) is { } serverContextCall)
        {
            return serverContextCall;
        }
        if (TryLowerKernelMethodInvocation(invocation) is { } kernelMethod)
        {
            return kernelMethod;
        }

        throw new NotSupportedException($"Server extension call '{invocation}' is not a host binding.");
    }

    private string? TryLowerServerContextHostBinding(
        InvocationExpressionSyntax invocation,
        IMethodSymbol? resolvedMethod)
    {
        if (resolvedMethod is not null)
        {
            return null;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax member ||
            !IsServerContextExpression(member.Expression) ||
            ServerContextHostBindingCandidates(member.Name.Identifier.ValueText, invocation.ArgumentList.Arguments) is not { Count: > 0 } candidates)
        {
            return null;
        }

        if (candidates.Count != 1)
        {
            throw new NotSupportedException(
                $"Server extension call '{invocation}' is ambiguous on server context type '{_serverContextType}'.");
        }

        var (method, binding) = candidates[0];
        AddBindingMetadata(binding);
        var args = LowerArgumentsInParameterOrder(
            invocation.ArgumentList.Arguments,
            method.Parameters,
            $"Host binding '{binding.BindingId}'");
        return Call(binding.BindingId, null, args);
    }

    private List<(IMethodSymbol Method, (string BindingId, string? Capability, IReadOnlyList<string> Effects, bool IsAsync) Binding)>
        ServerContextHostBindingCandidates(string methodName, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        var candidates = new List<(IMethodSymbol Method, (string BindingId, string? Capability, IReadOnlyList<string> Effects, bool IsAsync) Binding)>();
        if (_serverContextType is null)
        {
            return candidates;
        }

        foreach (var method in ServerContextMethods(methodName))
        {
            if (!CanBindArgumentsInParameterOrder(arguments, method.Parameters))
            {
                continue;
            }

            if (DotBoxDHostBindingExpressionLowerer.HostBinding(method, _model.Compilation) is { } binding)
            {
                candidates.Add((method, binding));
            }
        }

        return candidates;
    }

    private IEnumerable<IMethodSymbol> ServerContextMethods(string methodName)
    {
        if (_serverContextType is not INamedTypeSymbol named)
        {
            yield break;
        }

        for (INamedTypeSymbol? current = named; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                yield return method;
            }
        }

        foreach (var @interface in named.AllInterfaces)
        {
            foreach (var method in @interface.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                yield return method;
            }
        }
    }
    private string LowerMemberAccess(MemberAccessExpressionSyntax member)
    {
        if (TryLowerLiveSettingMemberAccess(member) is { } liveSetting)
        {
            return liveSetting;
        }

        var receiverType = TypeOf(member.Expression);
        if (string.Equals(member.Name.Identifier.ValueText, "Count", StringComparison.Ordinal) &&
            DotBoxDRpcTypeMapper.ListElementType(receiverType) is not null)
        {
            return Call("list.count", null, LowerExpression(member.Expression));
        }
        if (DotBoxDRpcTypeMapper.ListElementType(receiverType) is not null)
        {
            throw new NotSupportedException($"Server extension list member access '{member}' is not supported.");
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
    private ITypeSymbol TypeOf(ExpressionSyntax expression)
    {
        if (IsServerContextExpression(expression) && _serverContextType is { } serverContextType)
        {
            return serverContextType;
        }

        var type = _model.GetTypeInfo(expression, _cancellationToken);
        return type.Type
               ?? type.ConvertedType
               ?? throw new NotSupportedException($"Server extension could not resolve the type of '{expression}'.");
    }

    private bool IsStringExpression(ExpressionSyntax expression)
        => TypeOf(expression).SpecialType == SpecialType.System_String;

    private bool IsServerContextExpression(ExpressionSyntax expression)
        => expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => IsServerContextExpression(parenthesized.Expression),
            ThisExpressionSyntax => _serverContextType is not null && string.IsNullOrEmpty(_serverContextParameterName),
            IdentifierNameSyntax identifier => string.Equals(
                identifier.Identifier.ValueText,
                _serverContextParameterName,
                StringComparison.Ordinal),
            _ => false
        };

    private static bool HasDotBoxDServiceAttribute(ITypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (DotBoxDMetadataNames.IsRpcServiceAttribute(attribute.AttributeClass?.ToDisplayString()))
            {
                return true;
            }
        }

        return false;
    }

}

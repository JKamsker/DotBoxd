using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private string? TryLowerKernelMethodInvocation(InvocationExpressionSyntax invocation)
    {
        if (_model.GetSymbolInfo(invocation, _cancellationToken).Symbol is not IMethodSymbol resolvedMethod ||
            !HasKernelMethodAttribute(resolvedMethod, _model.Compilation))
        {
            return null;
        }

        var call = KernelMethodArgumentBinder.Bind(
            invocation,
            resolvedMethod,
            $"[KernelMethod] '{KernelMethodArgumentBinder.Definition(resolvedMethod).Name}'");
        var method = call.Method;
        if (!method.IsStatic && !IsServerContextReceiver(invocation, method))
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{method.Name}' used in InvokeAsync/server-extension IR must be static or called on the server context parameter.");
        }

        var returnType = DotBoxDTypeNameReader.KernelMethodTypeName(method.ReturnType);
        if (string.Equals(returnType, DotBoxDGenerationNames.ManifestTypes.Unsupported, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{method.Name}' must return a supported sandbox type.");
        }

        var methodKey = method.OriginalDefinition.ToDisplayString();
        if (_inlineStack is not null && _inlineStack.Contains(methodKey))
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{method.Name}' is recursive; recursive kernel methods cannot be inlined.");
        }

        var bindings = BindKernelMethodArguments(invocation, call);
        var body = KernelMethodBody(method);
        var bodyModel = _model.Compilation.GetSemanticModel(body.SyntaxTree);
        var lowerer = new DotBoxDRpcJsonLowerer(
            bodyModel,
            _capabilities,
            _effects,
            _cancellationToken,
            bindings,
            InlineStack(methodKey),
            _expressionPrelude,
            ReserveGeneratedLocal,
            _serverContextParameterName,
            _serverContextType);
        var lowered = lowerer.LowerExpression(body);
        Allocates |= lowerer.Allocates;

        if (!string.Equals(ExpressionTypeTag(body, bodyModel), returnType, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{method.Name}' body lowered to a different sandbox type than its return type.");
        }

        return lowered;
    }

    private IReadOnlyDictionary<string, RpcInlinedBinding> BindKernelMethodArguments(
        InvocationExpressionSyntax invocation,
        BoundKernelMethodCall call)
    {
        var bindings = new Dictionary<string, RpcInlinedBinding>(StringComparer.Ordinal);
        var invocationId = StableHash(
            call.Method.OriginalDefinition.ToDisplayString() + ":" +
            invocation.GetLocation().GetLineSpan().Path + ":" +
            invocation.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var parameterOrdinals = ParameterOrdinals(call.Method.Parameters);
        foreach (var argument in ArgumentsInEvaluationOrder(call))
        {
            var ordinal = parameterOrdinals[argument.Parameter.Name];
            if (DotBoxDNullableScalarType.IsNullableValueType(argument.Parameter.Type))
            {
                throw new NotSupportedException(
                    $"[KernelMethod] '{call.Method.Name}' nullable parameter '{argument.Parameter.Name}' is not supported in InvokeAsync/server-extension IR.");
            }

            var expected = DotBoxDTypeNameReader.KernelMethodTypeName(argument.Parameter.Type);
            if (string.Equals(expected, DotBoxDGenerationNames.ManifestTypes.Unsupported, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"[KernelMethod] '{call.Method.Name}' parameter '{argument.Parameter.Name}' must use a supported sandbox type.");
            }

            var actual = argument.Expression is { } expression
                ? ExpressionTypeTag(expression, _model)
                : expected;
            if (string.Equals(actual, DotBoxDGenerationNames.ManifestTypes.Unsupported, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"[KernelMethod] '{call.Method.Name}' argument {ordinal} must lower to a supported sandbox type.");
            }

            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"[KernelMethod] '{call.Method.Name}' argument {ordinal} must lower to {expected}.");
            }

            var localName = ReserveGeneratedLocal(
                "__sir_km_" + invocationId + "_" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AddExpressionPrelude(SetStatement(localName, LowerArgument(argument)));
            bindings[argument.Parameter.Name] = new RpcInlinedBinding(
                Var(localName),
                expected);
        }

        return bindings;
    }

    private string LowerArgument(BoundKernelMethodArgument argument)
        => argument.Expression is { } expression
            ? LowerExpression(expression)
            : LiteralJson(argument.Parameter, argument.DefaultValue);

    private string LiteralJson(IParameterSymbol parameter, object? value)
    {
        var converted = DotBoxDTypeNameReader.UnwrapTaskLike(parameter.Type);
        if (converted.SpecialType == SpecialType.System_Int64 && value is int i)
        {
            return LiteralJson((long)i);
        }

        if (converted.SpecialType is SpecialType.System_Double or SpecialType.System_Single &&
            value is IConvertible convertible)
        {
            return LiteralJson(convertible.ToDouble(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (DotBoxDNullableScalarType.IsNullableValueType(converted))
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{parameter.ContainingSymbol.Name}' optional nullable parameter '{parameter.Name}' is not supported in InvokeAsync/server-extension IR.");
        }

        return LiteralJson(value);
    }

    private ExpressionSyntax KernelMethodBody(IMethodSymbol method)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(_cancellationToken) is not MethodDeclarationSyntax declaration)
            {
                continue;
            }

            if (declaration.ExpressionBody?.Expression is { } expressionBody)
            {
                return expressionBody;
            }

            if (declaration.Body is { } block &&
                block.Statements.Count == 1 &&
                block.Statements[0] is ReturnStatementSyntax { Expression: { } returned })
            {
                return returned;
            }

            throw new NotSupportedException(
                $"[KernelMethod] '{method.Name}' must have an expression body or a single return statement.");
        }

        throw new NotSupportedException($"[KernelMethod] '{method.Name}' must be declared in source.");
    }

    private string ExpressionTypeTag(ExpressionSyntax expression, SemanticModel model)
    {
        var typeInfo = model.GetTypeInfo(expression, _cancellationToken);
        var type = typeInfo.ConvertedType ?? typeInfo.Type;
        if (type is null || type.TypeKind == TypeKind.Error)
        {
            throw new NotSupportedException($"[KernelMethod] expression '{expression}' has no supported type.");
        }

        return DotBoxDTypeNameReader.KernelMethodTypeName(type);
    }

    private bool IsServerContextReceiver(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (_serverContextParameterName is null ||
            _serverContextType is null ||
            invocation.Expression is not MemberAccessExpressionSyntax member ||
            member.Expression is not IdentifierNameSyntax receiver ||
            !string.Equals(receiver.Identifier.ValueText, _serverContextParameterName, StringComparison.Ordinal))
        {
            return false;
        }

        for (var current = _serverContextType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(method.ContainingType, current))
            {
                return true;
            }
        }

        return false;
    }

    private IReadOnlyCollection<string> InlineStack(string methodKey)
    {
        var stack = new HashSet<string>(StringComparer.Ordinal);
        if (_inlineStack is not null)
        {
            foreach (var entry in _inlineStack)
            {
                stack.Add(entry);
            }
        }

        stack.Add(methodKey);
        return stack;
    }

    private static bool HasKernelMethodAttribute(IMethodSymbol method, Compilation compilation)
    {
        var definition = KernelMethodArgumentBinder.Definition(method);
        foreach (var attribute in definition.GetAttributes())
        {
            if (compilation.GetTypeByMetadataName(DotBoxDMetadataNames.KernelMethodAttribute) is { } expected &&
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expected))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record RpcInlinedBinding(string Source, string Type);

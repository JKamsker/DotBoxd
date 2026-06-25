using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private string? TryLowerKernelMethodInvocation(InvocationExpressionSyntax invocation)
    {
        if (_model.GetSymbolInfo(invocation, _cancellationToken).Symbol is not IMethodSymbol method ||
            !HasKernelMethodAttribute(method, _model.Compilation))
        {
            return null;
        }

        if (!method.IsStatic)
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{method.Name}' used in InvokeAsync/server-extension IR must be static.");
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

        var bindings = BindKernelMethodArguments(invocation, method);
        var body = KernelMethodBody(method);
        var bodyModel = _model.Compilation.GetSemanticModel(body.SyntaxTree);
        var lowerer = new DotBoxDRpcJsonLowerer(
            bodyModel,
            _capabilities,
            _effects,
            _cancellationToken,
            bindings,
            InlineStack(methodKey));
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
        IMethodSymbol method)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != method.Parameters.Length)
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{method.Name}' call must pass {method.Parameters.Length} positional argument(s).");
        }

        var bindings = new Dictionary<string, RpcInlinedBinding>(StringComparer.Ordinal);
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].NameColon is not null ||
                !arguments[i].RefKindKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None))
            {
                throw new NotSupportedException(
                    $"[KernelMethod] '{method.Name}' arguments must be positional value arguments.");
            }

            var expected = DotBoxDTypeNameReader.KernelMethodTypeName(method.Parameters[i].Type);
            var actual = ExpressionTypeTag(arguments[i].Expression, _model);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"[KernelMethod] '{method.Name}' argument {i} must lower to {expected}.");
            }

            bindings[method.Parameters[i].Name] = new RpcInlinedBinding(
                LowerExpression(arguments[i].Expression),
                expected);
        }

        return bindings;
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
        foreach (var attribute in method.GetAttributes())
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

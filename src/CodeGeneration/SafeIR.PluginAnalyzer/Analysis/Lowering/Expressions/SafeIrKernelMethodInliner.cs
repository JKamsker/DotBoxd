namespace SafeIR.PluginAnalyzer;

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Inlines a call to a <c>[KernelMethod]</c>-annotated static helper into the calling kernel/hook IR.
/// The method's expression (or single-return) body is lowered with each parameter bound to the
/// already-lowered IR of the corresponding call-site argument, so the result is identical to writing the
/// body inline at the call site. Lets plugin authors factor shared gate/handler logic out of a
/// <c>Where</c>/<c>Select</c>/<c>InvokeKernel</c> lambda (or a kernel-class <c>ShouldHandle</c>/
/// <c>Handle</c>) while staying inside the sandbox.
/// <para>
/// A method without <c>[KernelMethod]</c> returns <see langword="null"/> so the caller can try the next
/// handler. Once the attribute is seen this lowerer owns the call: any unsupported shape (non-static,
/// non-scalar signature, multi-statement body, recursion, named/mismatched arguments) throws
/// <see cref="NotSupportedException"/>, which fails the whole chain/kernel safely rather than emitting a
/// miscompiled package.
/// </para>
/// </summary>
internal static class SafeIrKernelMethodInliner
{
    public static SafeIrExpressionModel? TryInline(
        InvocationExpressionSyntax invocation,
        SafeIrExpressionLoweringContext context,
        Func<ExpressionSyntax, SafeIrExpressionModel> lowerExpression)
    {
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is not IMethodSymbol method ||
            !HasKernelMethodAttribute(method))
        {
            return null;
        }

        if (!method.IsStatic)
        {
            throw new NotSupportedException($"[KernelMethod] '{method.Name}' must be static.");
        }

        var returnType = SafeIrTypeNameReader.SandboxTypeName(method.ReturnType);
        if (string.Equals(returnType, SafeIrGenerationNames.ManifestTypes.Unsupported, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{method.Name}' must return a supported scalar type.");
        }

        var methodKey = method.OriginalDefinition.ToDisplayString();
        if (context.IsInlining(methodKey))
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{method.Name}' is recursive; recursive kernel methods cannot be inlined.");
        }

        var bindings = BindArguments(invocation, method, lowerExpression);
        var body = KernelMethodBody(method, context.CancellationToken);
        var bodySemanticModel = context.SemanticModel.Compilation.GetSemanticModel(body.SyntaxTree);
        var inlineContext = context.ForInlinedMethod(bodySemanticModel, bindings, methodKey);
        var result = SafeIrExpressionModelFactory.Create(body, inlineContext);
        if (!string.Equals(result.Type, returnType, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{method.Name}' body lowered to {result.Type} but its return type is {returnType}.");
        }

        return result;
    }

    private static IReadOnlyDictionary<string, SafeIrExpressionModel> BindArguments(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        Func<ExpressionSyntax, SafeIrExpressionModel> lowerExpression)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != method.Parameters.Length)
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{method.Name}' call must pass {method.Parameters.Length} positional argument(s).");
        }

        var bindings = new Dictionary<string, SafeIrExpressionModel>(StringComparer.Ordinal);
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].NameColon is not null)
            {
                throw new NotSupportedException(
                    $"[KernelMethod] '{method.Name}' arguments must be positional.");
            }

            var lowered = lowerExpression(arguments[i].Expression);
            var expected = SafeIrTypeNameReader.SandboxTypeName(method.Parameters[i].Type);
            if (!string.Equals(lowered.Type, expected, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"[KernelMethod] '{method.Name}' argument {i} must lower to {expected}.");
            }

            bindings[method.Parameters[i].Name] = lowered;
        }

        return bindings;
    }

    private static ExpressionSyntax KernelMethodBody(IMethodSymbol method, CancellationToken cancellationToken)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration)
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

    private static bool HasKernelMethodAttribute(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    SafeIrGenerationNames.Metadata.KernelMethodAttribute,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

/// <summary>
/// Inlines a call to a <c>[KernelMethod]</c>-annotated helper into the calling kernel/hook IR.
/// The method's expression (or single-return) body is lowered with each parameter bound to the
/// already-lowered IR of the corresponding call-site argument, so the result is identical to writing the
/// body inline at the call site. Lets plugin authors factor shared gate/handler logic out of a
/// <c>Where</c>/<c>Select</c>/<c>Run</c> lambda (or a kernel-class <c>ShouldHandle</c>/
/// <c>Handle</c>) while staying inside the sandbox.
/// <para>
/// A method without <c>[KernelMethod]</c> returns <see langword="null"/> so the caller can try the next
/// handler. Once the attribute is seen this lowerer owns the call: any unsupported shape (non-static except
/// for the configured server context receiver, unsupported sandbox signature, multi-statement body, recursion,
/// named/mismatched arguments) throws
/// <see cref="NotSupportedException"/>, which fails the whole chain/kernel safely rather than emitting a
/// miscompiled package.
/// </para>
/// </summary>
internal static partial class DotBoxDKernelMethodInliner
{
    public static DotBoxDExpressionModel? TryInline(
        InvocationExpressionSyntax invocation,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (ResolveKernelMethodInvocation(invocation, context) is not { } resolvedMethod ||
            !HasKernelMethodAttribute(resolvedMethod, context.SemanticModel.Compilation))
        {
            return null;
        }

        var call = KernelMethodArgumentBinder.Bind(
            invocation,
            resolvedMethod,
            $"[KernelMethod] '{KernelMethodArgumentBinder.Definition(resolvedMethod).Name}'");
        var method = call.Method;
        if (!method.IsStatic && !IsServerContextReceiver(invocation, method, context))
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{method.Name}' must be static or called on the server context parameter.");
        }

        var returnType = DotBoxDTypeNameReader.KernelMethodTypeName(method.ReturnType);
        if (string.Equals(returnType, DotBoxDGenerationNames.ManifestTypes.Unsupported, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{method.Name}' must return a supported sandbox type.");
        }

        var methodKey = method.OriginalDefinition.ToDisplayString();
        if (context.IsInlining(methodKey))
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{method.Name}' is recursive; recursive kernel methods cannot be inlined.");
        }

        if (TryKernelMethodBody(method, context.CancellationToken) is { } body)
        {
            var bodySemanticModel = context.SemanticModel.Compilation.GetSemanticModel(body.SyntaxTree);
            KernelMethodArgumentReuseValidator.Validate(
                method,
                body,
                bodySemanticModel,
                call,
                context.SemanticModel,
                context.CancellationToken);
            var bindings = BindArguments(call, context, lowerExpression);
            var inlineContext = context.ForInlinedMethod(bodySemanticModel, bindings, methodKey);
            var result = DotBoxDExpressionModelFactory.Create(body, inlineContext);
            if (!string.Equals(result.Type, returnType, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"[KernelMethod] '{method.Name}' body lowered to {result.Type} but its return type is {returnType}.");
            }

            return result;
        }

        var descriptorBindings = BindArguments(call, context, lowerExpression);
        return InlineMetadataDescriptor(method, context, call, descriptorBindings, returnType);
    }

    private static IReadOnlyDictionary<string, DotBoxDExpressionModel> BindArguments(
        BoundKernelMethodCall call,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        var bindings = new Dictionary<string, DotBoxDExpressionModel>(StringComparer.Ordinal);
        for (var i = 0; i < call.Arguments.Count; i++)
        {
            var argument = call.Arguments[i];
            var expected = DotBoxDTypeNameReader.KernelMethodTypeName(argument.Parameter.Type);
            if (string.Equals(expected, DotBoxDGenerationNames.ManifestTypes.Unsupported, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"[KernelMethod] '{call.Method.Name}' parameter '{argument.Parameter.Name}' must use a supported sandbox type.");
            }

            var lowered = LowerArgument(argument, context, lowerExpression);
            if (string.Equals(lowered.Type, DotBoxDGenerationNames.ManifestTypes.Unsupported, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"[KernelMethod] '{call.Method.Name}' argument {i} must lower to a supported sandbox type.");
            }

            if (!string.Equals(lowered.Type, expected, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"[KernelMethod] '{call.Method.Name}' argument {i} must lower to {expected}.");
            }

            bindings[argument.Parameter.Name] = lowered;
        }

        return bindings;
    }

    private static DotBoxDExpressionModel LowerArgument(
        BoundKernelMethodArgument argument,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (argument.Expression is { } expression)
        {
            if (DotBoxDKernelMethodArgumentLowerer.TryLowerWholeEvent(argument.Parameter, expression, context)
                is { } wholeEvent)
            {
                return wholeEvent;
            }

            return DotBoxDNullableScalarExpressionLowerer.TryLower(
                expression,
                argument.Parameter.Type,
                context,
                lowerExpression,
                out var nullable)
                ? nullable
                : lowerExpression(expression);
        }

        return KernelMethodDefaultArgumentLowerer.Lower(argument.Parameter, argument.DefaultValue);
    }

    private static ExpressionSyntax? TryKernelMethodBody(IMethodSymbol method, CancellationToken cancellationToken)
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

        return null;
    }

    private static bool HasKernelMethodAttribute(IMethodSymbol method, Compilation compilation)
    {
        var definition = KernelMethodArgumentBinder.Definition(method);
        foreach (var attribute in definition.GetAttributes())
        {
            if (IsDotBoxDAttribute(attribute, compilation, DotBoxDMetadataNames.KernelMethodAttribute))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDotBoxDAttribute(AttributeData attribute, Compilation compilation, string metadataName)
        => compilation.GetTypeByMetadataName(metadataName) is { } expected &&
           SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expected);
}

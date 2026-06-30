using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDKernelMethodInliner
{
    public static DotBoxDHandleModel? TryInlineSendHandle(
        InvocationExpressionSyntax invocation,
        string contextParameterName,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is not IMethodSymbol resolvedMethod ||
            !HasKernelMethodAttribute(resolvedMethod, context.SemanticModel.Compilation))
        {
            return null;
        }

        var call = KernelMethodArgumentBinder.Bind(
            invocation,
            resolvedMethod,
            $"[KernelMethod] send helper '{KernelMethodArgumentBinder.Definition(resolvedMethod).Name}'");
        var method = call.Method;
        if (!method.IsStatic || !method.ReturnsVoid)
        {
            throw new NotSupportedException(
                $"[KernelMethod] send helper '{method.Name}' must be a static void method.");
        }

        var methodKey = method.OriginalDefinition.ToDisplayString();
        if (context.IsInlining(methodKey))
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{method.Name}' is recursive; recursive kernel methods cannot be inlined.");
        }

        var bindings = BindSendHelperArguments(
            call,
            contextParameterName,
            context,
            lowerExpression,
            out var helperContextParameterName);
        var sendInvocation = SendHelperInvocation(method, helperContextParameterName, context.CancellationToken);
        var bodySemanticModel = context.SemanticModel.Compilation.GetSemanticModel(sendInvocation.SyntaxTree);
        KernelMethodArgumentReuseValidator.Validate(
            method,
            sendInvocation,
            bodySemanticModel,
            call,
            context.SemanticModel,
            context.CancellationToken);
        var inlineContext = context.ForInlinedMethod(bodySemanticModel, bindings, methodKey);
        return DotBoxDHandleModelFactory.CreateFromSend(sendInvocation, inlineContext);
    }

    private static IReadOnlyDictionary<string, DotBoxDExpressionModel> BindSendHelperArguments(
        BoundKernelMethodCall call,
        string contextParameterName,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression,
        out string helperContextParameterName)
    {
        helperContextParameterName = string.Empty;
        var bindings = new Dictionary<string, DotBoxDExpressionModel>(StringComparer.Ordinal);
        for (var i = 0; i < call.Arguments.Count; i++)
        {
            var argument = call.Arguments[i];
            if (argument.Expression is { } expression &&
                IsContextArgument(expression, contextParameterName))
            {
                if (helperContextParameterName.Length != 0)
                {
                    throw new NotSupportedException(
                        $"[KernelMethod] send helper '{call.Method.Name}' must receive the hook context parameter exactly once.");
                }

                helperContextParameterName = argument.Parameter.Name;
                continue;
            }

            var lowered = LowerSendArgument(argument, context, lowerExpression);
            var expected = DotBoxDTypeNameReader.KernelMethodTypeName(argument.Parameter.Type);
            if (!string.Equals(lowered.Type, expected, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"[KernelMethod] '{call.Method.Name}' argument {i} must lower to {expected}.");
            }

            bindings[argument.Parameter.Name] = lowered;
        }

        if (helperContextParameterName.Length == 0)
        {
            throw new NotSupportedException(
                $"[KernelMethod] send helper '{call.Method.Name}' must receive the hook context parameter.");
        }

        return bindings;
    }

    private static DotBoxDExpressionModel LowerSendArgument(
        BoundKernelMethodArgument argument,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (argument.Expression is not { } expression)
        {
            return KernelMethodDefaultArgumentLowerer.Lower(argument.Parameter, argument.DefaultValue);
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

    private static InvocationExpressionSyntax SendHelperInvocation(
        IMethodSymbol method,
        string helperContextParameterName,
        CancellationToken cancellationToken)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration)
            {
                continue;
            }

            if (declaration.ExpressionBody?.Expression is InvocationExpressionSyntax expressionInvocation &&
                DotBoxDHandleModelFactory.IsContextSend(expressionInvocation.Expression, helperContextParameterName))
            {
                return expressionInvocation;
            }

            if (declaration.Body is { Statements.Count: 1 } block &&
                block.Statements[0] is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax bodyInvocation } &&
                DotBoxDHandleModelFactory.IsContextSend(bodyInvocation.Expression, helperContextParameterName))
            {
                return bodyInvocation;
            }

            throw new NotSupportedException(
                $"[KernelMethod] send helper '{method.Name}' must contain exactly one ctx.Messages.Send call.");
        }

        throw new NotSupportedException($"[KernelMethod] '{method.Name}' must be declared in source.");
    }

    private static bool IsContextArgument(ExpressionSyntax expression, string contextParameterName)
        => expression is IdentifierNameSyntax identifier &&
           string.Equals(identifier.Identifier.ValueText, contextParameterName, StringComparison.Ordinal);
}

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
                is not IMethodSymbol method ||
            !HasKernelMethodAttribute(method, context.SemanticModel.Compilation))
        {
            return null;
        }

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
            invocation,
            method,
            contextParameterName,
            lowerExpression,
            out var helperContextParameterName);
        var sendInvocation = SendHelperInvocation(method, helperContextParameterName, context.CancellationToken);
        var bodySemanticModel = context.SemanticModel.Compilation.GetSemanticModel(sendInvocation.SyntaxTree);
        var inlineContext = context.ForInlinedMethod(bodySemanticModel, bindings, methodKey);
        return DotBoxDHandleModelFactory.CreateFromSend(sendInvocation, inlineContext);
    }

    private static IReadOnlyDictionary<string, DotBoxDExpressionModel> BindSendHelperArguments(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        string contextParameterName,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression,
        out string helperContextParameterName)
    {
        helperContextParameterName = string.Empty;
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != method.Parameters.Length)
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{method.Name}' call must pass {method.Parameters.Length} positional argument(s).");
        }

        var bindings = new Dictionary<string, DotBoxDExpressionModel>(StringComparer.Ordinal);
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].NameColon is not null ||
                !arguments[i].RefKindKeyword.IsKind(SyntaxKind.None))
            {
                throw new NotSupportedException(
                    $"[KernelMethod] '{method.Name}' arguments must be positional value arguments.");
            }

            if (IsContextArgument(arguments[i].Expression, contextParameterName))
            {
                if (helperContextParameterName.Length != 0)
                {
                    throw new NotSupportedException(
                        $"[KernelMethod] send helper '{method.Name}' must receive the hook context parameter exactly once.");
                }

                helperContextParameterName = method.Parameters[i].Name;
                continue;
            }

            var lowered = lowerExpression(arguments[i].Expression);
            var expected = DotBoxDTypeNameReader.KernelMethodTypeName(method.Parameters[i].Type);
            if (!string.Equals(lowered.Type, expected, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"[KernelMethod] '{method.Name}' argument {i} must lower to {expected}.");
            }

            bindings[method.Parameters[i].Name] = lowered;
        }

        if (helperContextParameterName.Length == 0)
        {
            throw new NotSupportedException(
                $"[KernelMethod] send helper '{method.Name}' must receive the hook context parameter.");
        }

        return bindings;
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

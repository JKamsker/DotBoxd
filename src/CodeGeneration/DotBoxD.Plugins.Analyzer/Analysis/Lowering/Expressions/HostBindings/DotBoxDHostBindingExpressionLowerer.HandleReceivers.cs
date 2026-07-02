using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDHostBindingExpressionLowerer
{
    private static IReadOnlyList<DotBoxDExpressionModel> LowerHostBindingCallArguments(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        string bindingId,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (TryResolveHandleReceiver(invocation, method, context) is not { } receiver)
        {
            return LowerHostBindingArguments(invocation.ArgumentList.Arguments, method.Parameters, bindingId, lowerExpression);
        }

        var factoryArguments = LowerHostBindingArguments(
            receiver.Invocation.ArgumentList.Arguments,
            receiver.Method.Parameters,
            bindingId,
            lowerExpression);
        var handleArguments = LowerHostBindingArguments(
            invocation.ArgumentList.Arguments,
            method.Parameters,
            bindingId,
            lowerExpression);
        return factoryArguments.Concat(handleArguments).ToArray();
    }

    private static HostHandleReceiver? TryResolveHandleReceiver(
        InvocationExpressionSyntax invocation,
        IMethodSymbol handleMethod,
        DotBoxDExpressionLoweringContext context)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Expression: InvocationExpressionSyntax receiverInvocation
            } ||
            ResolveHostBindingInvocation(receiverInvocation, context) is not { } receiver ||
            !ReturnsHandleType(receiver.Method.ReturnType, handleMethod.ContainingType))
        {
            return null;
        }

        return new HostHandleReceiver(receiverInvocation, receiver.Method);
    }

    private static bool ReturnsHandleType(ITypeSymbol returnType, INamedTypeSymbol handleType)
    {
        var unwrapped = DotBoxDTypeNameReader.UnwrapTaskLike(returnType);
        return SymbolEqualityComparer.Default.Equals(unwrapped, handleType) ||
               unwrapped is INamedTypeSymbol named &&
               named.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, handleType));
    }

    private readonly record struct HostHandleReceiver(
        InvocationExpressionSyntax Invocation,
        IMethodSymbol Method);
}

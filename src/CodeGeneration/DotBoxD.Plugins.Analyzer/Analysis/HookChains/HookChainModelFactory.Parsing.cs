using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    private static HookChainInterceptorInstallKind? InstallKind(
        string terminalMethod,
        HookChainReceiverKind? receiverKind,
        GeneratedRemoteHookChainKind? generatedRemoteKind)
        => terminalMethod switch
        {
            RunMethod => HookChainInterceptorInstallKind.GeneratedChain,
            RunLocalMethod when receiverKind == HookChainReceiverKind.Remote || generatedRemoteKind is not null =>
                HookChainInterceptorInstallKind.LocalCallback,
            RegisterMethod when receiverKind is HookChainReceiverKind.Local or HookChainReceiverKind.Remote =>
                HookChainInterceptorInstallKind.ResultChain,
            RegisterMethod when generatedRemoteKind == GeneratedRemoteHookChainKind.Hook =>
                HookChainInterceptorInstallKind.ResultChain,
            RegisterLocalMethod when receiverKind is HookChainReceiverKind.Local or HookChainReceiverKind.Remote =>
                HookChainInterceptorInstallKind.LocalResultChain,
            RegisterLocalMethod when generatedRemoteKind == GeneratedRemoteHookChainKind.Hook =>
                HookChainInterceptorInstallKind.LocalResultChain,
            _ => null
        };

    private static InvocationExpressionSyntax? WalkToSeed(ExpressionSyntax receiver, List<HookChainStage> stages)
    {
        var current = receiver;
        while (current is InvocationExpressionSyntax invocation &&
               invocation.Expression is MemberAccessExpressionSyntax access)
        {
            var name = access.Name.Identifier.ValueText;
            if (string.Equals(name, OnMethod, StringComparison.Ordinal))
            {
                return invocation;
            }

            var isSelect = string.Equals(name, SelectMethod, StringComparison.Ordinal);
            if ((isSelect || string.Equals(name, WhereMethod, StringComparison.Ordinal)) &&
                TryLambda(invocation, out var lambda))
            {
                stages.Add(new HookChainStage(isSelect, lambda));
                current = access.Expression;
                continue;
            }

            return null;
        }

        return null;
    }

    private static HookChainReceiverKind? ReceiverKind(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken)
    {
        if (model.GetTypeInfo(receiver, cancellationToken).Type is not INamedTypeSymbol type)
        {
            return null;
        }

        return ReceiverKind(type);
    }

    private static HookChainReceiverKind? ReceiverKind(INamedTypeSymbol type)
    {
        var original = type.OriginalDefinition.ToDisplayString();
        if (string.Equals(original, DotBoxDGenerationNames.TypeNames.RemoteHookPipelineOriginal, StringComparison.Ordinal) ||
            string.Equals(original, DotBoxDGenerationNames.TypeNames.RemoteHookStageOriginal, StringComparison.Ordinal) ||
            string.Equals(original, DotBoxDGenerationNames.TypeNames.RemoteSubscriptionPipelineOriginal, StringComparison.Ordinal) ||
            string.Equals(original, DotBoxDGenerationNames.TypeNames.RemoteSubscriptionStageOriginal, StringComparison.Ordinal))
        {
            return HookChainReceiverKind.Remote;
        }

        if (string.Equals(original, DotBoxDGenerationNames.TypeNames.HookPipelineOriginal, StringComparison.Ordinal) ||
            string.Equals(original, DotBoxDGenerationNames.TypeNames.HookStageOriginal, StringComparison.Ordinal) ||
            string.Equals(original, DotBoxDGenerationNames.TypeNames.SubscriptionPipelineOriginal, StringComparison.Ordinal) ||
            string.Equals(original, DotBoxDGenerationNames.TypeNames.SubscriptionStageOriginal, StringComparison.Ordinal))
        {
            return HookChainReceiverKind.Local;
        }

        return null;
    }

    // Accepts both lambda forms a fluent stage can take: a parenthesized lambda (e), (e, ctx) or (),
    // and the simple form e => .... Arity is resolved later by LambdaParameters, so every stage
    // independently chooses element-only or element+context regardless of what neighbouring stages used.
    private static bool TryLambda(InvocationExpressionSyntax invocation, out LambdaExpressionSyntax lambda)
    {
        lambda = null!;
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 1 ||
            arguments[0].Expression is not LambdaExpressionSyntax lambdaExpression)
        {
            return false;
        }

        lambda = lambdaExpression;
        return true;
    }

    // The handler lambda of a result terminal - Register(lambda, priority) / RegisterLocal(lambda, priority) -
    // allowing a named/reordered handler argument such as Register(priority: 10, handler: ctx => ...).
    private static bool TryLeadingLambda(InvocationExpressionSyntax invocation, out LambdaExpressionSyntax lambda)
    {
        lambda = null!;
        var arguments = invocation.ArgumentList.Arguments;
        LambdaExpressionSyntax? firstUnnamedLambda = null;
        foreach (var argument in arguments)
        {
            if (argument.NameColon is { Name.Identifier.ValueText: "handler" })
            {
                if (argument.Expression is not LambdaExpressionSyntax namedHandler)
                {
                    return false;
                }

                lambda = namedHandler;
                return true;
            }

            if (argument.NameColon is null &&
                firstUnnamedLambda is null &&
                argument.Expression is LambdaExpressionSyntax unnamedHandler)
            {
                firstUnnamedLambda = unnamedHandler;
            }
        }

        if (firstUnnamedLambda is null)
        {
            return false;
        }

        lambda = firstUnnamedLambda;
        return true;
    }

    // Element-only lambdas (e =>, (e) =>) yield (element, null, null); element+context lambdas ((e, ctx) =>)
    // yield (element, context, null). A cancellation parameter is accepted for RegisterLocal result terminals
    // and rejected by ResultHookChain for other result shapes.
    private static (string? ElementParam, string? ContextParam, string? CancellationParam) LambdaParameters(
        LambdaExpressionSyntax lambda)
    {
        switch (lambda)
        {
            case SimpleLambdaExpressionSyntax simple:
                return (simple.Parameter.Identifier.ValueText, null, null);
            case ParenthesizedLambdaExpressionSyntax parenthesized:
                var parameters = parenthesized.ParameterList.Parameters;
                return parameters.Count switch
                {
                    1 => (parameters[0].Identifier.ValueText, null, null),
                    2 => (parameters[0].Identifier.ValueText, parameters[1].Identifier.ValueText, null),
                    3 => (
                        parameters[0].Identifier.ValueText,
                        parameters[1].Identifier.ValueText,
                        parameters[2].Identifier.ValueText),
                    _ => (null, null, null),
                };
            default:
                return (null, null, null);
        }
    }

    private static bool ContainsUnsupported(EquatableArray<EventPropertyModel> eventProperties)
    {
        for (var i = 0; i < eventProperties.Count; i++)
        {
            if (eventProperties[i].Type == DotBoxDGenerationNames.ManifestTypes.Unsupported)
            {
                return true;
            }
        }

        return false;
    }
}

using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    private static HookChainInterceptorInstallKind? InstallKind(
        PipelineStepRole? terminalRole,
        HookChainReceiverKind? receiverKind,
        GeneratedRemoteHookChainKind? generatedRemoteKind)
        => terminalRole switch
        {
            PipelineStepRole.Run => HookChainInterceptorInstallKind.GeneratedChain,
            PipelineStepRole.RunLocal when receiverKind == HookChainReceiverKind.Remote || generatedRemoteKind is not null =>
                HookChainInterceptorInstallKind.LocalCallback,
            PipelineStepRole.Register when receiverKind is HookChainReceiverKind.Local or HookChainReceiverKind.Remote =>
                HookChainInterceptorInstallKind.ResultChain,
            PipelineStepRole.Register when generatedRemoteKind == GeneratedRemoteHookChainKind.Hook =>
                HookChainInterceptorInstallKind.ResultChain,
            PipelineStepRole.RegisterLocal when receiverKind is HookChainReceiverKind.Local or HookChainReceiverKind.Remote =>
                HookChainInterceptorInstallKind.LocalResultChain,
            PipelineStepRole.RegisterLocal when generatedRemoteKind == GeneratedRemoteHookChainKind.Hook =>
                HookChainInterceptorInstallKind.LocalResultChain,
            _ => null
        };

    // Resolves the pipeline role of a resolved method purely from its [PipelineStep] attribute. The framework
    // marks its own pipeline methods (see [PipelineStep]/[PipelineSurface] on the Runtime pipeline types), so
    // no method-name literal drives recognition once a symbol (or overload candidate) is available.
    private static PipelineStepRole? RoleOf(IMethodSymbol? method, Compilation compilation)
        => PipelineRoleReader.RoleOf(method, compilation);

    private static PipelineStepRole? LegacyNameRole(string? methodName)
        => methodName switch
        {
            OnMethod => PipelineStepRole.Seed,
            WhereMethod => PipelineStepRole.Filter,
            SelectMethod => PipelineStepRole.Projection,
            RunMethod => PipelineStepRole.Run,
            RunLocalMethod => PipelineStepRole.RunLocal,
            RegisterMethod => PipelineStepRole.Register,
            RegisterLocalMethod => PipelineStepRole.RegisterLocal,
            _ => null,
        };

    private static PipelineStepRole? RoleOf(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var info = model.GetSymbolInfo(invocation, cancellationToken);
        var symbol = info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : null);
        if (RoleOf(symbol as IMethodSymbol, model.Compilation) is { } role)
        {
            return role;
        }

        // A method on a type that DID opt into [PipelineSurface] but left this method unmarked is deliberately
        // NOT a pipeline step: the per-method [PipelineStep] opt-in is enforced, so we must not resurrect it by
        // name (that would silently lower a consumer's ordinary, standard-named method on their surface type).
        // Fall back to syntactic name recognition only for receivers OUTSIDE the attribute vocabulary: a
        // generated/not-yet-emitted registry (no symbol at all) or a prebuilt/legacy SDK type that predates
        // [PipelineStep] (a resolved symbol whose containing type is not itself a [PipelineSurface]).
        if (symbol is IMethodSymbol resolved &&
            resolved.ContainingType is { } containingType &&
            ReceiverKind(containingType, model.Compilation) is not null)
        {
            return null;
        }

        return invocation.Expression is MemberAccessExpressionSyntax access
            ? LegacyNameRole(access.Name.Identifier.ValueText)
            : null;
    }

    private static InvocationExpressionSyntax? WalkToSeed(
        ExpressionSyntax receiver,
        List<HookChainStage> stages,
        SemanticModel model,
        CancellationToken cancellationToken)
        => WalkToSeed(receiver, stages, model, cancellationToken, depth: 0);

    private static InvocationExpressionSyntax? WalkToSeed(
        ExpressionSyntax receiver,
        List<HookChainStage> stages,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
    {
        if (depth > 8)
        {
            return null;
        }

        receiver = HookChainAliasResolver.UnwrapTransparentExpression(receiver);

        if (HookChainAliasResolver.Initializer(receiver, model, cancellationToken) is { } initializer)
        {
            return WalkToSeed(initializer, stages, model, cancellationToken, depth + 1);
        }

        var current = receiver;
        while (true)
        {
            current = HookChainAliasResolver.UnwrapTransparentExpression(current);
            if (HookChainAliasResolver.Initializer(current, model, cancellationToken) is { } currentInitializer)
            {
                current = currentInitializer;
                continue;
            }

            if (current is not InvocationExpressionSyntax invocation ||
                invocation.Expression is not MemberAccessExpressionSyntax access)
            {
                return null;
            }

            var role = RoleOf(invocation, model, cancellationToken);
            if (role == PipelineStepRole.Seed)
            {
                return invocation;
            }

            if (role is PipelineStepRole.Filter or PipelineStepRole.Projection &&
                TryLambda(invocation, out var lambda))
            {
                if (IsResolvedNonDotBoxDStageMethodInvocation(invocation, model, cancellationToken))
                {
                    return null;
                }

                stages.Add(new HookChainStage(role == PipelineStepRole.Projection, lambda));
                current = HookChainAliasResolver.UnwrapTransparentExpression(access.Expression);
                continue;
            }

            return null;
        }
    }

    private static bool IsResolvedNonDotBoxDStageMethodInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
        => model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method &&
           (method.ContainingType is null || ReceiverKind(method.ContainingType, model.Compilation) is null);

    internal static HookChainReceiverKind? ReceiverKind(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken)
    {
        if (model.GetTypeInfo(receiver, cancellationToken).Type is not INamedTypeSymbol type)
        {
            return null;
        }

        return ReceiverKind(type, model.Compilation);
    }

    // Transport declared by [PipelineSurface] on the receiver type. Every framework pipeline/stage type carries
    // the attribute (enforced by PipelineStepMarkingContractTests), so a null result means the receiver is not a
    // pipeline surface at all — an unmarked consumer type or an unrelated type — and the chain is left alone.
    internal static HookChainReceiverKind? ReceiverKind(INamedTypeSymbol type, Compilation compilation)
        => PipelineRoleReader.Transport(type, compilation);

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

    // Element-only lambdas (e =>, (e) =>) yield (element, null); element+context lambdas ((e, ctx) =>)
    // yield (element, context). Other arities are unsupported.
    private static (string? ElementParam, string? ContextParam) LambdaParameters(
        LambdaExpressionSyntax lambda)
    {
        switch (lambda)
        {
            case SimpleLambdaExpressionSyntax simple:
                return (simple.Parameter.Identifier.ValueText, null);
            case ParenthesizedLambdaExpressionSyntax parenthesized:
                var parameters = parenthesized.ParameterList.Parameters;
                return parameters.Count switch
                {
                    1 => (parameters[0].Identifier.ValueText, null),
                    2 => (parameters[0].Identifier.ValueText, parameters[1].Identifier.ValueText),
                    _ => (null, null),
                };
            default:
                return (null, null);
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

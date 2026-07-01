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

    // Resolves the pipeline role of a fluent call from its [PipelineStep] attribute, falling back to the
    // framework's own method names while the library's pipeline methods are being annotated. The name fallback
    // is transitional; once every framework pipeline method carries [PipelineStep] the LegacyNameRole map (and
    // the *Method name constants) are deleted so no method-name literal drives recognition.
    private static PipelineStepRole? RoleOf(IMethodSymbol? method, Compilation compilation)
        => PipelineRoleReader.RoleOf(method, compilation) ?? LegacyNameRole(method?.Name);

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

        // A generated or unbound receiver — a not-yet-emitted plugin-server registry, or a terminal whose
        // shape binds to no overload — exposes no symbol that could carry [PipelineStep]. Recognize the
        // framework's own pipeline methods by their syntactic name there so lowering and the not-lowered
        // diagnostics still fire; a consumer's custom-named surface participates through the attribute path.
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

    // Transport declared by [PipelineSurface] on the receiver type, falling back to the framework's own
    // pipeline/stage type names while they are being annotated. The fallback is transitional and is deleted
    // (together with LegacyReceiverKind) once every framework surface carries [PipelineSurface].
    internal static HookChainReceiverKind? ReceiverKind(INamedTypeSymbol type, Compilation compilation)
        => PipelineRoleReader.Transport(type, compilation) ?? LegacyReceiverKind(type);

    private static HookChainReceiverKind? LegacyReceiverKind(INamedTypeSymbol type)
    {
        var original = type.OriginalDefinition.ToDisplayString();
        if (string.Equals(original, DotBoxDGenerationNames.TypeNames.RemoteHookPipelineOriginal, StringComparison.Ordinal) ||
            string.Equals(original, DotBoxDGenerationNames.TypeNames.RemoteHookPipelineWithContextOriginal, StringComparison.Ordinal) ||
            string.Equals(original, DotBoxDGenerationNames.TypeNames.RemoteHookStageOriginal, StringComparison.Ordinal) ||
            string.Equals(original, DotBoxDGenerationNames.TypeNames.RemoteHookStageWithContextOriginal, StringComparison.Ordinal) ||
            string.Equals(original, DotBoxDGenerationNames.TypeNames.RemoteSubscriptionPipelineOriginal, StringComparison.Ordinal) ||
            string.Equals(original, DotBoxDGenerationNames.TypeNames.RemoteSubscriptionPipelineWithContextOriginal, StringComparison.Ordinal) ||
            string.Equals(original, DotBoxDGenerationNames.TypeNames.RemoteSubscriptionStageOriginal, StringComparison.Ordinal) ||
            string.Equals(original, DotBoxDGenerationNames.TypeNames.RemoteSubscriptionStageWithContextOriginal, StringComparison.Ordinal))
        {
            return HookChainReceiverKind.Remote;
        }

        if (string.Equals(original, DotBoxDGenerationNames.TypeNames.HookPipelineWithContextOriginal, StringComparison.Ordinal) ||
            string.Equals(original, DotBoxDGenerationNames.TypeNames.HookStageWithContextOriginal, StringComparison.Ordinal) ||
            string.Equals(original, DotBoxDGenerationNames.TypeNames.SubscriptionPipelineWithContextOriginal, StringComparison.Ordinal) ||
            string.Equals(original, DotBoxDGenerationNames.TypeNames.SubscriptionStageWithContextOriginal, StringComparison.Ordinal))
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

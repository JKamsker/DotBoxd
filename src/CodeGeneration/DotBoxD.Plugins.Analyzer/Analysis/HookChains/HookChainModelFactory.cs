using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

/// <summary>
/// Phase C lowering of an inline hook chain —
/// <c>On&lt;TEvent&gt;().Where*(lambda).Select*(lambda).Run(lambda)</c> — into the same
/// <see cref="PluginKernelModel"/> a kernel class produces, so the existing emitter + verifier path
/// applies unchanged. The <c>Where</c>s AND-compose into <c>ShouldHandle</c>; a <c>Select</c> projects
/// the flowing element and downstream lambdas substitute that projection at compile time (via the
/// lowering context's projected-element binding); the <c>Run</c> terminal's single
/// <c>ctx.Messages.Send(targetId, message)</c> becomes <c>Handle</c>. Supported subset: expression-body
/// lambdas and a single Send terminal. Any other shape fails safe (returns <c>null</c>, no package),
/// leaving the runtime terminal to throw DBXK062 / the analyzer to flag DBXK110.
/// </summary>
internal static class HookChainModelFactory
{
    private const string RunMethod = "Run";
    private const string RunLocalMethod = "RunLocal";
    private const string WhereMethod = "Where";
    private const string SelectMethod = "Select";
    private const string OnMethod = "On";

    public static HookChainResult? Create(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return null;
        }

        try
        {
            return TryCreate(invocation, context.SemanticModel, cancellationToken);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static HookChainResult? TryCreate(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax terminalAccess)
        {
            return null;
        }

        var terminalMethod = terminalAccess.Name.Identifier.ValueText;
        var stages = new List<HookChainStage>();
        var seed = WalkToSeed(terminalAccess.Expression, stages);
        if (seed is null)
        {
            return null;
        }

        var receiverKind = ReceiverKind(model, terminalAccess.Expression, cancellationToken);
        var receiverIsKnownHookChain = receiverKind is not null;
        var generatedRemoteCandidate = receiverIsKnownHookChain
            ? null
            : GeneratedRemoteHookChainFallback.CandidateKind(seed);
        if (!receiverIsKnownHookChain && generatedRemoteCandidate is null)
        {
            return null;
        }

        var generatedRemoteKind = receiverIsKnownHookChain ? null : generatedRemoteCandidate;
        var installKind = InstallKind(terminalMethod, receiverKind, generatedRemoteKind);
        if (installKind is null)
        {
            return null;
        }

        if (!TryLambda(invocation, out var terminalLambda))
        {
            return null;
        }

        var (terminalElementParam, terminalContextParam) = LambdaParameters(terminalLambda);
        if (terminalElementParam is null)
        {
            return null;
        }

        stages.Reverse(); // seed-to-terminal order

        if (!GeneratedRemoteHookChainFallback.TryEventType(model, seed, cancellationToken, out var eventType))
        {
            return null;
        }

        var eventProperties = PluginSymbolReader.EventProperties(eventType);
        if (ContainsUnsupported(eventProperties))
        {
            return null;
        }

        // Collectors for the whole chain: every Where/Select/terminal-Send deposits the capabilities its
        // IR needs (Send, [HostBinding] calls, gated event-property reads) and every extra sandbox effect
        // a [HostBinding] declares. Sorted for deterministic, incrementality-stable output.
        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        var effects = new SortedSet<string>(StringComparer.Ordinal);

        var terminalElementTypeFullName = TerminalElementTypeFullName(
            stages,
            eventProperties,
            eventType,
            model,
                cancellationToken);
        var shouldHandle = HookChainStageLowerer.CreateShouldHandle(
            stages,
            eventProperties,
            model,
            cancellationToken,
            capabilities,
            effects);
        var localCallbackProjection = installKind == HookChainInterceptorInstallKind.LocalCallback
            ? HookChainStageLowerer.CreateProjection(
                stages,
                eventProperties,
                model,
                cancellationToken,
                capabilities,
                effects)
            : null;
        var handleBody = installKind == HookChainInterceptorInstallKind.LocalCallback
            ? LocalCallbackHandleBody(localCallbackProjection)
            : LowerSendHandle(
                stages,
                terminalLambda,
                terminalElementParam,
                terminalContextParam,
                eventProperties,
                model,
                cancellationToken,
                capabilities,
                effects);
        var handleReturnType = installKind == HookChainInterceptorInstallKind.LocalCallback
            ? LocalCallbackHandleReturnType(localCallbackProjection)
            : DotBoxDGenerationNames.TypeNames.GlobalSandboxType + ".Unit";

        var (indexPredicates, indexCoversPredicate) = HookChainIndexPredicateExtractor.Extract(
            stages,
            eventProperties,
            model,
            cancellationToken);

        var chainId = HookChainIdentity.Compute(invocation);
        var kernelName = "HookChain_" + chainId;
        var modelResult = new PluginKernelModel(
            PluginId: "chain-" + chainId,
            Namespace: HookChainIdentity.Namespace(invocation),
            KernelName: kernelName,
            PackageName: kernelName + "PluginPackage",
            EventName: EventTypeName.Qualified(eventType),
            EventParameterName: DotBoxDGenerationNames.DefaultEventParameterName,
            ContextParameterName: terminalContextParam ?? DotBoxDGenerationNames.DefaultContextParameterName,
            HandleEventParameterName: terminalElementParam,
            HandleContextParameterName: terminalContextParam ?? DotBoxDGenerationNames.DefaultContextParameterName,
            EventProperties: eventProperties,
            LiveSettings: default,
            ShouldHandle: shouldHandle,
            HandleBody: handleBody,
            HandleReturnTypeSource: handleReturnType,
            ManifestEffects: ManifestEffects(installKind.Value, shouldHandle, handleBody, effects),
            RequiredCapabilities: EquatableArray<string>.FromOwned([.. capabilities]),
            IndexPredicates: indexPredicates,
            IndexCoversPredicate: indexCoversPredicate);

        return new HookChainResult(
            modelResult,
            Interception(
                invocation,
                model,
                modelResult,
                terminalAccess.Expression,
                eventType,
                stages,
                terminalElementTypeFullName,
                generatedRemoteKind,
                installKind.Value,
                cancellationToken));
    }

    private static HookChainInterceptorInstallKind? InstallKind(
        string terminalMethod,
        HookChainReceiverKind? receiverKind,
        GeneratedRemoteHookChainKind? generatedRemoteKind)
        => terminalMethod switch
        {
            RunMethod => HookChainInterceptorInstallKind.GeneratedChain,
            RunLocalMethod when receiverKind == HookChainReceiverKind.Remote || generatedRemoteKind is not null =>
                HookChainInterceptorInstallKind.LocalCallback,
            _ => null
        };

    private static EquatableArray<string> ManifestEffects(
        HookChainInterceptorInstallKind installKind,
        DotBoxDStatementBodyModel shouldHandle,
        DotBoxDStatementBodyModel handleBody,
        ICollection<string> effects)
        => installKind == HookChainInterceptorInstallKind.LocalCallback
            ? DotBoxDManifestEffectModel.CreateLocalCallback(shouldHandle, handleBody, effects)
            : DotBoxDManifestEffectModel.Create(shouldHandle, handleBody, effects);

    private static DotBoxDStatementBodyModel LocalCallbackHandleBody(HookChainProjection? projection)
        => projection is null
            ? DotBoxDHandleBodyModelFactory.ReturnUnit()
            : DotBoxDHandleBodyModelFactory.ReturnExpression(projection.Value, projection.Prefix);

    private static string LocalCallbackHandleReturnType(HookChainProjection? projection)
        => projection is null
            ? DotBoxDGenerationNames.TypeNames.GlobalSandboxType + ".Unit"
            : $"TypeOf({LiteralReader.StringLiteral(projection.Value.Type)})";

    private static DotBoxDStatementBodyModel LowerSendHandle(
        IReadOnlyList<HookChainStage> stages,
        LambdaExpressionSyntax terminalLambda,
        string terminalElementParam,
        string? terminalContextParam,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        if (terminalContextParam is null ||
            terminalLambda.ExpressionBody is not InvocationExpressionSyntax sendInvocation ||
            !DotBoxDHandleModelFactory.IsContextSend(sendInvocation.Expression, terminalContextParam))
        {
            throw new NotSupportedException();
        }

        var handle = HookChainStageLowerer.CreateHandle(
            stages,
            terminalElementParam,
            sendInvocation,
            eventProperties,
            model,
            cancellationToken,
            capabilities,
            effects);
        return DotBoxDHandleBodyModelFactory.FromSend(handle);
    }

    private static HookChainInterception? Interception(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        PluginKernelModel chainModel,
        ExpressionSyntax receiver,
        INamedTypeSymbol eventType,
        IReadOnlyList<HookChainStage> stages,
        string terminalElementTypeFullName,
        GeneratedRemoteHookChainKind? generatedRemoteKind,
        HookChainInterceptorInstallKind installKind,
        CancellationToken cancellationToken)
    {
        var location = model.GetInterceptableLocation(invocation, cancellationToken);
        if (location is null)
        {
            return null;
        }

        var packageFullName = string.IsNullOrEmpty(chainModel.Namespace)
            ? DotBoxDGenerationNames.TypeNames.GlobalPrefix + chainModel.PackageName
            : DotBoxDGenerationNames.TypeNames.GlobalPrefix + chainModel.Namespace + "." + chainModel.PackageName;

        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method &&
            method.Parameters.Length == 1 &&
            model.GetTypeInfo(receiver, cancellationToken).Type is INamedTypeSymbol receiverType &&
            ReceiverKind(receiverType) is not null)
        {
            return new HookChainInterception(
                location.GetInterceptsLocationAttributeSyntax(),
                receiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                packageFullName,
                installKind);
        }

        if (generatedRemoteKind is null)
        {
            return null;
        }

        return GeneratedRemoteHookChainFallback.CreateInterception(
            location.GetInterceptsLocationAttributeSyntax(),
            eventType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            stages.Any(stage => stage.IsSelect),
            terminalElementTypeFullName,
            packageFullName,
            installKind,
            generatedRemoteKind.Value);
    }

    private static string TerminalElementTypeFullName(
        IReadOnlyList<HookChainStage> stages,
        EquatableArray<EventPropertyModel> eventProperties,
        INamedTypeSymbol eventType,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        DotBoxDExpressionModel? projected = null;
        var terminalElementTypeFullName = eventType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        foreach (var stage in stages)
        {
            if (!stage.IsSelect)
            {
                continue;
            }

            var (elementParam, _) = LambdaParameters(stage.Lambda);
            if (elementParam is null || stage.Lambda.ExpressionBody is not { } body)
            {
                throw new NotSupportedException();
            }

            var scratchCapabilities = new SortedSet<string>(StringComparer.Ordinal);
            var scratchEffects = new SortedSet<string>(StringComparer.Ordinal);
            projected = DotBoxDExpressionModelFactory.Create(
                body,
                Context(elementParam, eventProperties, projected, model, cancellationToken, scratchCapabilities, scratchEffects));
            terminalElementTypeFullName = GeneratedRemoteHookChainFallback.TypeFullName(
                body,
                model,
                cancellationToken,
                projected.Type);
        }

        return terminalElementTypeFullName;
    }

    private static DotBoxDExpressionLoweringContext Context(
        string elementParam,
        EquatableArray<EventPropertyModel> eventProperties,
        DotBoxDExpressionModel? projected,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
        => projected is null
            ? new DotBoxDExpressionLoweringContext(
                elementParam, eventProperties, default, model, cancellationToken,
                capabilities: capabilities, effects: effects)
            : new DotBoxDExpressionLoweringContext(
                elementParam, eventProperties, default, model, cancellationToken, elementParam, projected,
                capabilities, effects);

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
    // and the simple form e => …. Arity is resolved later by LambdaParameters, so every stage
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

    // Element-only lambdas (e =>, (e) =>) yield (element, null); element+context lambdas ((e, ctx) =>)
    // yield (element, context). A null element means an unsupported shape (zero or 3+ parameters) — the
    // caller fails safe. The context being null is fine for Where/Select (they never reference ctx); the
    // terminal Send separately requires a non-null context, so an element-only terminal won't lower.
    private static (string? ElementParam, string? ContextParam) LambdaParameters(LambdaExpressionSyntax lambda)
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

internal enum HookChainReceiverKind
{
    Local,
    Remote
}

namespace SafeIR.PluginAnalyzer;

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Phase C lowering of an inline hook chain —
/// <c>On&lt;TEvent&gt;().Where*(lambda).Select*(lambda).InvokeKernel(lambda)</c> — into the same
/// <see cref="PluginKernelModel"/> a kernel class produces, so the existing emitter + verifier path
/// applies unchanged. The <c>Where</c>s AND-compose into <c>ShouldHandle</c>; a <c>Select</c> projects
/// the flowing element and downstream lambdas substitute that projection at compile time (via the
/// lowering context's projected-element binding); the <c>InvokeKernel</c> terminal's single
/// <c>ctx.Messages.Send(targetId, message)</c> becomes <c>Handle</c>. Supported subset: expression-body
/// lambdas and a single Send terminal. Any other shape fails safe (returns <c>null</c>, no package),
/// leaving the runtime terminal to throw SGP062 / the analyzer to flag SGP110.
/// </summary>
internal static class HookChainModelFactory
{
    private const string InvokeKernelMethod = "InvokeKernel";
    private const string WhereMethod = "Where";
    private const string SelectMethod = "Select";
    private const string OnMethod = "On";
    private const string HookPipelineOriginal = "SafeIR.Plugins.HookPipeline<TEvent>";
    private const string HookStageOriginal = "SafeIR.Plugins.HookStage<TEvent, TCurrent>";

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
        if (invocation.Expression is not MemberAccessExpressionSyntax terminalAccess ||
            !string.Equals(terminalAccess.Name.Identifier.ValueText, InvokeKernelMethod, StringComparison.Ordinal) ||
            !IsHookChainType(model, terminalAccess.Expression, cancellationToken, out var receiverIsPipeline))
        {
            return null;
        }

        if (!TryLambda(invocation, out var terminalLambda) ||
            terminalLambda.ExpressionBody is not InvocationExpressionSyntax sendInvocation)
        {
            return null;
        }

        var (terminalElementParam, terminalContextParam) = LambdaParameters(terminalLambda);
        if (terminalElementParam is null || terminalContextParam is null ||
            !SafeIrHandleModelFactory.IsContextSend(sendInvocation.Expression, terminalContextParam))
        {
            return null;
        }

        var stages = new List<Stage>();
        var seed = WalkToSeed(terminalAccess.Expression, stages);
        if (seed is null)
        {
            return null;
        }

        stages.Reverse(); // seed-to-terminal order

        if (model.GetTypeInfo(seed, cancellationToken).Type is not INamedTypeSymbol pipelineType ||
            pipelineType.TypeArguments.Length != 1 ||
            pipelineType.TypeArguments[0] is not INamedTypeSymbol eventType)
        {
            return null;
        }

        var eventProperties = PluginSymbolReader.EventProperties(eventType);
        if (eventProperties.Count == 0)
        {
            return null;
        }

        // Collectors for the whole chain: every Where/Select/terminal-Send deposits the capabilities its
        // IR needs (Send, [HostBinding] calls, gated event-property reads) and every extra sandbox effect
        // a [HostBinding] declares. Sorted for deterministic, incrementality-stable output.
        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        var effects = new SortedSet<string>(StringComparer.Ordinal);

        // Forward pass: track the projected-element binding; record each Where with the context that
        // was current at its position (event mode, or projected after a Select).
        var whereStages = new List<(ExpressionSyntax Body, SafeIrExpressionLoweringContext Context)>();
        SafeIrExpressionModel? projected = null;
        var shouldHandleEventParam = SafeIrGenerationNames.DefaultEventParameterName;

        foreach (var stage in stages)
        {
            var (elementParam, _) = LambdaParameters(stage.Lambda);
            if (elementParam is null || stage.Lambda.ExpressionBody is not { } body)
            {
                return null;
            }

            var context = Context(elementParam, eventProperties, projected, model, cancellationToken, capabilities, effects);
            if (stage.IsSelect)
            {
                projected = SafeIrExpressionModelFactory.Create(body, context);
            }
            else
            {
                whereStages.Add((body, context));
                if (projected is null)
                {
                    shouldHandleEventParam = elementParam;
                }
            }
        }

        // AND-compose the Where conditions in source order: fold from the last so the first Where is
        // the outermost branch (if w0 then (if w1 then ... else false) else false).
        var shouldHandle = SafeIrConditionBodyModelFactory.AlwaysTrue();
        for (var i = whereStages.Count - 1; i >= 0; i--)
        {
            shouldHandle = SafeIrConditionBodyModelFactory.CreateBranch(
                whereStages[i].Body,
                shouldHandle,
                SafeIrConditionBodyModelFactory.AlwaysFalse(),
                whereStages[i].Context);
        }

        var handleContext = Context(terminalElementParam, eventProperties, projected, model, cancellationToken, capabilities, effects);
        var handle = SafeIrHandleModelFactory.CreateFromSend(sendInvocation, handleContext);

        var chainId = HookChainIdentity.Compute(invocation);
        var kernelName = "HookChain_" + chainId;
        var modelResult = new PluginKernelModel(
            PluginId: "chain-" + chainId,
            Namespace: HookChainIdentity.Namespace(invocation),
            KernelName: kernelName,
            PackageName: kernelName + "PluginPackage",
            EventName: eventType.MetadataName,
            EventParameterName: shouldHandleEventParam,
            ContextParameterName: terminalContextParam,
            HandleEventParameterName: terminalElementParam,
            HandleContextParameterName: terminalContextParam,
            EventProperties: eventProperties,
            LiveSettings: default,
            ShouldHandle: shouldHandle,
            Handle: handle,
            ManifestEffects: SafeIrManifestEffectModel.Create(shouldHandle, handle, effects),
            RequiredCapabilities: EquatableArray<string>.FromOwned([.. capabilities]));

        return new HookChainResult(modelResult, Interception(invocation, model, eventType, modelResult, receiverIsPipeline, cancellationToken));
    }

    private static HookChainInterception? Interception(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        INamedTypeSymbol eventType,
        PluginKernelModel chainModel,
        bool receiverIsPipeline,
        CancellationToken cancellationToken)
    {
        // Only HookPipeline<TEvent> terminals get an interceptor in the MVP (a HookStage after a Select
        // has a second type parameter the non-generic interceptor cannot name).
        if (!receiverIsPipeline)
        {
            return null;
        }

        var location = model.GetInterceptableLocation(invocation, cancellationToken);
        if (location is null)
        {
            return null;
        }

        var handlerIsAction = model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method &&
            method.Parameters.Length == 1 &&
            method.Parameters[0].Type.OriginalDefinition.ToDisplayString().StartsWith("System.Action", StringComparison.Ordinal);

        var packageFullName = string.IsNullOrEmpty(chainModel.Namespace)
            ? "global::" + chainModel.PackageName
            : "global::" + chainModel.Namespace + "." + chainModel.PackageName;

        return new HookChainInterception(
            location.GetInterceptsLocationAttributeSyntax(),
            eventType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            packageFullName,
            handlerIsAction);
    }

    private static SafeIrExpressionLoweringContext Context(
        string elementParam,
        EquatableArray<EventPropertyModel> eventProperties,
        SafeIrExpressionModel? projected,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
        => projected is null
            ? new SafeIrExpressionLoweringContext(
                elementParam, eventProperties, default, model, cancellationToken,
                capabilities: capabilities, effects: effects)
            : new SafeIrExpressionLoweringContext(
                elementParam, eventProperties, default, model, cancellationToken, elementParam, projected,
                capabilities, effects);

    private static InvocationExpressionSyntax? WalkToSeed(ExpressionSyntax receiver, List<Stage> stages)
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
                stages.Add(new Stage(isSelect, lambda));
                current = access.Expression;
                continue;
            }

            return null;
        }

        return null;
    }

    private static bool IsHookChainType(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out bool isPipeline)
    {
        isPipeline = false;
        if (model.GetTypeInfo(receiver, cancellationToken).Type is not INamedTypeSymbol type)
        {
            return false;
        }

        var original = type.OriginalDefinition.ToDisplayString();
        isPipeline = string.Equals(original, HookPipelineOriginal, StringComparison.Ordinal);
        return isPipeline || string.Equals(original, HookStageOriginal, StringComparison.Ordinal);
    }

    private static bool TryLambda(InvocationExpressionSyntax invocation, out ParenthesizedLambdaExpressionSyntax lambda)
    {
        lambda = null!;
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 1 ||
            arguments[0].Expression is not ParenthesizedLambdaExpressionSyntax parenthesized)
        {
            return false;
        }

        lambda = parenthesized;
        return true;
    }

    private static (string? ElementParam, string? ContextParam) LambdaParameters(ParenthesizedLambdaExpressionSyntax lambda)
    {
        var parameters = lambda.ParameterList.Parameters;
        return parameters.Count == 2
            ? (parameters[0].Identifier.ValueText, parameters[1].Identifier.ValueText)
            : (null, null);
    }

    private readonly struct Stage
    {
        public Stage(bool isSelect, ParenthesizedLambdaExpressionSyntax lambda)
        {
            IsSelect = isSelect;
            Lambda = lambda;
        }

        public bool IsSelect { get; }

        public ParenthesizedLambdaExpressionSyntax Lambda { get; }
    }
}

using DotBoxD.Plugins.Analyzer.Analysis.HookResults;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

/// <summary>
/// Lowers a result-returning hook chain — <c>On&lt;TContext&gt;().Where*(lambda).Register(lambda, priority)</c>
/// or <c>.RegisterLocal(lambda, priority)</c> — to a <see cref="PluginKernelModel"/>. The result type is taken
/// from <c>[Hook]</c> on the context. A <c>Register</c> chain reuses the projection package shape: the
/// <c>Where</c> filter lowers to <c>ShouldHandle</c> and the result-producing lambda body lowers to a
/// <c>Handle</c> that returns the result record (so the validator's projection-Handle path applies); the host
/// installs it via <c>UseGeneratedResultChain</c> and decodes the result rather than pushing it. A
/// <c>RegisterLocal</c> chain lowers only the filter (whole-event shape, Unit Handle); the plugin delegate
/// produces the result. Any unsupported shape throws <see cref="NotSupportedException"/> so the chain fails
/// safe (no package, DBXK113).
/// </summary>
internal static partial class ResultHookChain
{
    public static HookChainResult? Build(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax receiver,
        SemanticModel model,
        CancellationToken cancellationToken,
        IReadOnlyList<HookChainStage> stages,
        INamedTypeSymbol contextType,
        EquatableArray<EventPropertyModel> eventProperties,
        LambdaExpressionSyntax terminalLambda,
        string terminalElementParam,
        string? terminalContextParam,
        bool terminalHasCancellationToken,
        bool isLocal,
        GeneratedRemoteHookChainKind? generatedRemoteKind,
        string? generatedRemoteServerContextTypeFullName)
    {
        // A Select before the result terminal would re-type the flowing element; v1 supports only Where filters.
        foreach (var stage in stages)
        {
            if (stage.IsSelect)
            {
                throw new NotSupportedException();
            }
        }

        if (!TryResolveHook(contextType, out var hookName, out var resultType) ||
            !HookResultModelFactory.CanSatisfyHookResult(resultType, model.Compilation, cancellationToken))
        {
            throw new NotSupportedException();
        }

        if (terminalHasCancellationToken && !isLocal)
        {
            throw new NotSupportedException();
        }

        // The handler's TResult is NOT read from the Register call symbol: when the handler body is a fluent
        // builder (Result.Ok().With…()) its return type is generator-added and unresolved here, so the call's
        // inferred type argument is unavailable. The result type comes from [Hook] instead, and the handler body's
        // type is validated against it in LowerResultHandle (the seed type for a fluent chain).

        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        var shouldHandle = HookChainStageLowerer.CreateShouldHandle(
            stages, eventProperties, model, cancellationToken, capabilities, effects);

        DotBoxDStatementBodyModel handleBody;
        string handleReturnType;
        var terminalServerContextType = terminalContextParam is null
            ? null
            : LambdaParameterType(terminalLambda, terminalContextParam, model, cancellationToken);
        if (isLocal)
        {
            ResultHookLocalHandlerValidator.EnsureReturnsHookResult(
                terminalLambda,
                resultType,
                model,
                cancellationToken);

            // RegisterLocal: only the filter is verified IR; the Handle returns Unit and the plugin delegate
            // produces the result through the generated local result installation path.
            handleBody = DotBoxDHandleBodyModelFactory.ReturnUnit();
            handleReturnType = TypeNames.GlobalSandboxType + ".Unit";
        }
        else
        {
            handleBody = LowerResultHandle(
                terminalLambda,
                terminalElementParam,
                terminalContextParam,
                terminalServerContextType,
                resultType,
                eventProperties,
                model,
                cancellationToken,
                capabilities,
                effects);
            handleReturnType = SandboxTypeSourceEmitter.TryEmit(resultType)
                ?? throw new NotSupportedException();
        }

        var (indexPredicates, indexCoversPredicate) = HookChainIndexPredicateExtractor.Extract(
            stages, eventProperties, model, cancellationToken);

        var chainId = HookChainIdentity.Compute(invocation);
        var kernelName = "HookChain_" + chainId;
        var kernelModel = new PluginKernelModel(
            PluginId: "chain-" + chainId,
            Namespace: HookChainIdentity.Namespace(invocation),
            KernelName: kernelName,
            PackageName: kernelName + DotBoxDGenerationNames.PluginPackageSuffix,
            EventName: hookName,
            EventParameterName: DotBoxDGenerationNames.DefaultEventParameterName,
            ContextParameterName: terminalContextParam ?? DotBoxDGenerationNames.DefaultContextParameterName,
            HandleEventParameterName: terminalElementParam,
            HandleContextParameterName: terminalContextParam ?? DotBoxDGenerationNames.DefaultContextParameterName,
            EventProperties: eventProperties,
            LiveSettings: default,
            ShouldHandle: shouldHandle,
            HandleBody: handleBody,
            HandleReturnTypeSource: handleReturnType,
            ManifestEffects: DotBoxDManifestEffectModel.CreateLocalCallback(shouldHandle, handleBody, effects),
            RequiredCapabilities: EquatableArray<string>.FromOwned([.. capabilities]),
            IndexPredicates: indexPredicates,
            IndexCoversPredicate: indexCoversPredicate)
        {
            ResultType = resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ResultLocalTerminal = isLocal,
        };

        return new HookChainResult(kernelModel, Interception(
            invocation,
            receiver,
            model,
            kernelModel,
            contextType,
            resultType,
            isLocal,
            terminalHasCancellationToken,
            terminalContextParam is not null,
            receiverIsStage: false,
            generatedRemoteKind,
            generatedRemoteServerContextTypeFullName,
            cancellationToken));
    }

    public static bool IsResultTerminal(string terminalMethod)
        => string.Equals(terminalMethod, "Register", StringComparison.Ordinal)
            || string.Equals(terminalMethod, "RegisterLocal", StringComparison.Ordinal);

    private static DotBoxDStatementBodyModel LowerResultHandle(
        LambdaExpressionSyntax terminalLambda,
        string terminalElementParam,
        string? terminalContextParam,
        ITypeSymbol? terminalContextType,
        INamedTypeSymbol resultType,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        if (terminalLambda.ExpressionBody is not { } body)
        {
            throw new NotSupportedException();
        }

        // The lowered body must construct the associated result record exactly. For an object initializer
        // (new Result { ... }) the body type resolves directly; for a fluent builder (Result.Ok().With…())
        // the trailing builder method is generator-added and not visible while this chain is lowered, so the
        // body type is null/error — fall back to the builder chain's seed type.
        var bodyType = model.GetTypeInfo(body, cancellationToken).ConvertedType
            ?? model.GetTypeInfo(body, cancellationToken).Type;
        if (bodyType is { TypeKind: not TypeKind.Error })
        {
            if (!SymbolEqualityComparer.Default.Equals(bodyType, resultType))
            {
                throw new NotSupportedException();
            }
        }
        else if (body is not InvocationExpressionSyntax builderChain ||
            !SymbolEqualityComparer.Default.Equals(
                DotBoxDResultBuilderExpressionLowerer.ResolveSeedResultType(builderChain, model, cancellationToken),
                resultType))
        {
            throw new NotSupportedException();
        }

        var context = new DotBoxDExpressionLoweringContext(
            terminalElementParam, eventProperties, default, model, cancellationToken,
            serverContextParameterName: terminalContextParam,
            serverContextType: terminalContextType,
            capabilities: capabilities, effects: effects);
        var lowered = DotBoxDExpressionModelFactory.Create(body, context);
        if (!string.Equals(lowered.Type, DotBoxDGenerationNames.ManifestTypes.Record, StringComparison.Ordinal))
        {
            throw new NotSupportedException();
        }

        return DotBoxDHandleBodyModelFactory.ReturnExpression(lowered);
    }

    private static bool TryResolveHook(
        INamedTypeSymbol contextType,
        out string hookName,
        out INamedTypeSymbol resultType)
    {
        hookName = string.Empty;
        resultType = null!;
        foreach (var attribute in contextType.GetAttributes())
        {
            if (string.Equals(attribute.AttributeClass?.ToDisplayString(), DotBoxDMetadataNames.HookAttribute, StringComparison.Ordinal) &&
                attribute.ConstructorArguments.Length == 2 &&
                attribute.ConstructorArguments[0].Value is string declaredName &&
                !string.IsNullOrWhiteSpace(declaredName) &&
                attribute.ConstructorArguments[1].Value is INamedTypeSymbol declaredResult)
            {
                hookName = declaredName;
                resultType = declaredResult;
                return true;
            }
        }

        return false;
    }

    private static ITypeSymbol? LambdaParameterType(
        LambdaExpressionSyntax lambda,
        string parameterName,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (lambda is not ParenthesizedLambdaExpressionSyntax parenthesized)
        {
            return null;
        }

        foreach (var parameter in parenthesized.ParameterList.Parameters)
        {
            if (string.Equals(parameter.Identifier.ValueText, parameterName, StringComparison.Ordinal))
            {
                return (model.GetDeclaredSymbol(parameter, cancellationToken) as IParameterSymbol)?.Type;
            }
        }

        return null;
    }

}

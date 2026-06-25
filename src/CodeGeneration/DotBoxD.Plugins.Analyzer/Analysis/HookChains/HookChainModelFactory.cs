using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
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
/// lambdas and a direct Send terminal or static <c>[KernelMethod]</c> Send helper. Any other shape fails safe
/// (returns <c>null</c>, no package),
/// leaving the runtime terminal to throw DBXK062 / the generator to report DBXK114.
/// </summary>
internal static partial class HookChainModelFactory
{
    private const string RunMethod = "Run";
    private const string RunLocalMethod = "RunLocal";
    private const string RegisterMethod = "Register";
    private const string RegisterLocalMethod = "RegisterLocal";
    private const string WhereMethod = "Where";
    private const string SelectMethod = "Select";
    private const string OnMethod = "On";

    public static HookChainCreateResult? Create(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return null;
        }

        HookChainResult? chain;
        string? notLoweredDetail = null;
        try
        {
            chain = TryCreate(invocation, context.SemanticModel, cancellationToken);
        }
        catch (NotSupportedException ex)
        {
            chain = null;
            notLoweredDetail = ex.Message;
        }

        if (chain is not null)
        {
            return new HookChainCreateResult(chain, null);
        }

        return NotLoweredDiagnostic(invocation, context.SemanticModel, cancellationToken, notLoweredDetail);
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
        var generatedRemoteTarget = receiverIsKnownHookChain
            ? null
            : GeneratedRemoteHookChainFallback.Candidate(seed, model, cancellationToken);
        if (!receiverIsKnownHookChain && generatedRemoteTarget is null)
        {
            return null;
        }

        var generatedRemoteKind = receiverIsKnownHookChain ? null : generatedRemoteTarget?.Kind;
        var generatedRemoteServerContextTypeFullName = generatedRemoteTarget is { } target
            ? GeneratedRemoteHookChainFallback.ServerContextTypeFullName(model, seed, target, cancellationToken)
            : null;
        var installKind = InstallKind(terminalMethod, receiverKind, generatedRemoteKind);
        if (installKind is null)
        {
            return null;
        }

        // Run/RunLocal take a single lambda; Register/RegisterLocal take (lambda, priority) — accept the leading
        // lambda for the result terminals so the trailing priority argument does not reject the chain.
        var isResultTerminal = installKind is HookChainInterceptorInstallKind.ResultChain
            or HookChainInterceptorInstallKind.LocalResultChain;
        if (!(isResultTerminal ? TryLeadingLambda(invocation, out var terminalLambda) : TryLambda(invocation, out terminalLambda)))
        {
            return null;
        }

        var (terminalElementParam, terminalContextParam, terminalIsAsyncLocal, terminalHasCancellationToken) =
            installKind == HookChainInterceptorInstallKind.LocalResultChain
                ? ResultLocalLambdaParameters(invocation, terminalLambda, model, cancellationToken)
                : ResultLocalTerminalShape.From(LambdaParameters(terminalLambda));
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

        // Result-returning hooks (Register/RegisterLocal) lower the filter the same way, but the Handle returns
        // the result record (Register) or Unit with an in-process delegate (RegisterLocal); they install via the
        // result-chain entrypoints. Delegated to keep the Send-terminal path below focused.
        if (installKind is HookChainInterceptorInstallKind.ResultChain or HookChainInterceptorInstallKind.LocalResultChain)
        {
            return ResultHookChain.Build(
                invocation,
                terminalAccess.Expression,
                model,
                cancellationToken,
                stages,
                eventType,
                eventProperties,
                terminalLambda,
                terminalElementParam,
                terminalContextParam,
                installKind == HookChainInterceptorInstallKind.LocalResultChain,
                terminalIsAsyncLocal,
                terminalHasCancellationToken,
                generatedRemoteKind,
                generatedRemoteServerContextTypeFullName);
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
        var terminalContextType = terminalContextParam is null
            ? null
            : LambdaParameterType(terminalLambda, terminalContextParam, model, cancellationToken);
        var shouldHandle = HookChainStageLowerer.CreateShouldHandle(
            stages,
            eventProperties,
            model,
            cancellationToken,
            capabilities,
            effects);
        var localCallbackProjection = installKind == HookChainInterceptorInstallKind.LocalCallback
            ? LocalCallbackProjection(
                stages,
                eventProperties,
                eventType,
                model,
                cancellationToken,
                capabilities,
                effects)
            : null;
        var handleBody = installKind == HookChainInterceptorInstallKind.LocalCallback
            ? LocalCallbackHandleBody(localCallbackProjection)
            : LowerRunHandle(
                stages,
                terminalLambda,
                terminalElementParam,
                terminalContextParam,
                terminalContextType,
                eventProperties,
                model,
                cancellationToken,
                capabilities,
                effects);
        // The CLR type the pushed value carries: the final Select element, or the whole event when there is no
        // Select. Resolved once and reused for both the Handle return SandboxType (projection chains) and the
        // reflection-free decoder. Null only for a non-local chain.
        var projectedTypeSymbol = installKind == HookChainInterceptorInstallKind.LocalCallback
            ? ProjectedTypeSymbol(stages, eventType, model, cancellationToken)
            : null;

        // An anonymous type CAN be the terminal (pushed) projection — it has a real metadata identity Roslyn can
        // infer as a type ARGUMENT — but it has no C#-source-nameable name. The interceptor handles it by binding
        // the projection slot as a generic type PARAMETER. The local decoder is generic too and creates a matching
        // anonymous-object literal, so it still avoids the reflective registration path.
        var projectionIsUnnameable = projectedTypeSymbol is INamedTypeSymbol { IsAnonymousType: true };

        var handleReturnType = installKind == HookChainInterceptorInstallKind.LocalCallback
            ? LocalCallbackHandleReturnType(localCallbackProjection, projectedTypeSymbol)
            : DotBoxDGenerationNames.TypeNames.GlobalSandboxType + ".Unit";

        // The reflection-free decoder for the pushed value's projected type (final Select element, or the whole
        // event when there is no Select). Null for a non-local chain or a type that is not wire-eligible — those
        // keep the reflective 2-arg registration so they do not regress.
        var localDecoderSource = installKind == HookChainInterceptorInstallKind.LocalCallback
            ? BuildLocalDecoderSource(projectedTypeSymbol)
            : null;

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
            EventName: EventTypeName.HookOrQualified(eventType),
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
            IndexCoversPredicate: indexCoversPredicate)
        {
            // Persist the local-terminal nature in the manifest (a host-readable mark) so the runtime knows to
            // push rather than run. Even no-Select RunLocal chains are emitted as an explicit event-record
            // projection, so ordinary Unit-returning Run packages cannot be relabeled into native callbacks.
            LocalTerminal = installKind == HookChainInterceptorInstallKind.LocalCallback,
            ProjectedType = localCallbackProjection?.Value.Type,
            LocalDecoderSource = localDecoderSource,
        };

        var interception = Interception(
            invocation,
            model,
            modelResult,
            terminalAccess.Expression,
            eventType,
            stages,
            terminalElementTypeFullName,
            generatedRemoteKind,
            installKind.Value,
            generatedRemoteServerContextTypeFullName,
            terminalContextParam is not null,
            TerminalReturnsVoid(terminalLambda, model, cancellationToken),
            localDecoderSource is not null,
            projectedTypeSymbol,
            cancellationToken);
        if (interception is null)
        {
            throw new NotSupportedException(InterceptionFailureDetail(generatedRemoteKind, projectedTypeSymbol));
        }

        return new HookChainResult(modelResult, interception);
    }

    private static string InterceptionFailureDetail(
        GeneratedRemoteHookChainKind? generatedRemoteKind,
        ITypeSymbol? projectedTypeSymbol)
        => generatedRemoteKind is not null &&
           projectedTypeSymbol is INamedTypeSymbol { IsAnonymousType: true }
            ? "anonymous terminal projections on generated-server chains require a named record projection"
            : "the call site is not interceptable";

}

internal enum HookChainReceiverKind
{
    Local,
    Remote
}

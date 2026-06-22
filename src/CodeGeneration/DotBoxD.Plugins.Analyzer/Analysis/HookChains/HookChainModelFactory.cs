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
        try
        {
            chain = TryCreate(invocation, context.SemanticModel, cancellationToken);
        }
        catch (NotSupportedException)
        {
            chain = null;
        }

        if (chain is not null)
        {
            return new HookChainCreateResult(chain, null);
        }

        // No package was emitted. Either the call site is not a recognized chain (nothing to do), or it IS a remote
        // RunLocal chain whose Where/Select stages could not be lowered. Only the latter leaves the native terminal
        // to throw NotSupportedException at runtime, so surface a build-time diagnostic for exactly that case.
        if (TryRemoteRunLocalLocation(invocation, context.SemanticModel, cancellationToken, out var location))
        {
            return new HookChainCreateResult(null, new HookChainNotLoweredDiagnostic(location));
        }

        // A recognized in-process result hook (On<TContext>().Register/RegisterLocal) that produced no package
        // leaves its native terminal to throw at runtime; surface DBXK113 so the cause shows at build time.
        if (TryResultChainLocation(invocation, context.SemanticModel, cancellationToken, out var resultLocation, out var isLocalTerminal))
        {
            return new HookChainCreateResult(
                null,
                new HookChainNotLoweredDiagnostic(resultLocation, ResultChain: true, LocalResultTerminal: isLocalTerminal));
        }

        return null;
    }

    // True when the call site is a Register/RegisterLocal terminal on a known hook pipeline — the surface whose
    // native terminal throws when the generator does not intercept it.
    private static bool TryResultChainLocation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        out PluginDiagnosticLocation location,
        out bool isLocalTerminal)
    {
        location = default;
        isLocalTerminal = false;
        if (invocation.Expression is not MemberAccessExpressionSyntax terminalAccess ||
            (!string.Equals(terminalAccess.Name.Identifier.ValueText, RegisterMethod, StringComparison.Ordinal) &&
             !string.Equals(terminalAccess.Name.Identifier.ValueText, RegisterLocalMethod, StringComparison.Ordinal)))
        {
            return false;
        }

        var receiverKind = ReceiverKind(model, terminalAccess.Expression, cancellationToken);
        if (receiverKind is not (HookChainReceiverKind.Local or HookChainReceiverKind.Remote))
        {
            var stages = new List<HookChainStage>();
            var seed = WalkToSeed(terminalAccess.Expression, stages);
            if (seed is null || GeneratedRemoteHookChainFallback.CandidateKind(seed) != GeneratedRemoteHookChainKind.Hook)
            {
                return false;
            }
        }

        isLocalTerminal = string.Equals(terminalAccess.Name.Identifier.ValueText, RegisterLocalMethod, StringComparison.Ordinal);
        location = PluginDiagnosticLocation.From(terminalAccess.Name.GetLocation());
        return true;
    }

    // True when the call site is a remote RunLocal terminal: RunLocal whose receiver's static type is one of the
    // remote hook/subscription stage/pipeline types. Those (and only those) throw NotSupportedException when the
    // generator does not intercept them, so a remote RunLocal that produced no package will throw at runtime.
    private static bool TryRemoteRunLocalLocation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        out PluginDiagnosticLocation location)
    {
        location = default;
        if (invocation.Expression is not MemberAccessExpressionSyntax terminalAccess ||
            !string.Equals(terminalAccess.Name.Identifier.ValueText, RunLocalMethod, StringComparison.Ordinal) ||
            ReceiverKind(model, terminalAccess.Expression, cancellationToken) != HookChainReceiverKind.Remote)
        {
            return false;
        }

        location = PluginDiagnosticLocation.From(terminalAccess.Name.GetLocation());
        return true;
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

        // Run/RunLocal take a single lambda; Register/RegisterLocal take (lambda, priority) — accept the leading
        // lambda for the result terminals so the trailing priority argument does not reject the chain.
        var isResultTerminal = installKind is HookChainInterceptorInstallKind.ResultChain
            or HookChainInterceptorInstallKind.LocalResultChain;
        if (!(isResultTerminal ? TryLeadingLambda(invocation, out var terminalLambda) : TryLambda(invocation, out terminalLambda)))
        {
            return null;
        }

        var (terminalElementParam, terminalContextParam, terminalCancellationParam) = LambdaParameters(terminalLambda);
        if (terminalElementParam is null)
        {
            return null;
        }

        if (terminalCancellationParam is not null &&
            installKind != HookChainInterceptorInstallKind.LocalResultChain)
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
                terminalCancellationParam is not null,
                installKind == HookChainInterceptorInstallKind.LocalResultChain,
                generatedRemoteKind);
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
            IndexCoversPredicate: indexCoversPredicate)
        {
            // Persist the local-terminal nature in the manifest (mine's host-readable mark) so the runtime
            // knows to push rather than run; a null ProjectedType (no Select) is a whole-event push, a
            // non-null one is a projection push — so the payload kind needs no separate persisted field.
            LocalTerminal = installKind == HookChainInterceptorInstallKind.LocalCallback,
            ProjectedType = localCallbackProjection?.Value.Type,
            LocalDecoderSource = localDecoderSource,
        };

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
                localDecoderSource is not null,
                projectedTypeSymbol,
                cancellationToken));
    }

    // Generates the reflection-free reader for the pushed value's projected type, mirroring the type the
    // terminal handler receives: the final Select element type, or the whole event when there is no Select.
    // Returns null when the type is not wire-eligible (the reader emitter declines) so the chain falls back to
    // the reflective registration.
    private static string? BuildLocalDecoderSource(ITypeSymbol? projectedTypeSymbol)
        => projectedTypeSymbol is null
            ? null
            : Rpc.RpcLocalDecoderEmitter.TryEmit(projectedTypeSymbol);

    // The CLR type the pushed value decodes to: the final Select body's type (using the same
    // ConvertedType ?? Type resolution as TerminalElementTypeFullName so the generated ReadProjected return
    // type matches the handler's projected type), or the event type when the chain has no Select.
    private static ITypeSymbol? ProjectedTypeSymbol(
        IReadOnlyList<HookChainStage> stages,
        INamedTypeSymbol eventType,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        ITypeSymbol projected = eventType;
        foreach (var stage in stages)
        {
            if (!stage.IsSelect)
            {
                continue;
            }

            if (stage.Lambda.ExpressionBody is not { } body)
            {
                return null;
            }

            var typeInfo = model.GetTypeInfo(body, cancellationToken);
            var type = typeInfo.ConvertedType ?? typeInfo.Type;
            if (type is null || type.TypeKind == TypeKind.Error)
            {
                return null;
            }

            projected = type;
        }

        return projected;
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

    private static string LocalCallbackHandleReturnType(
        HookChainProjection? projection,
        ITypeSymbol? projectedTypeSymbol)
    {
        if (projection is null)
        {
            // Whole-event push: the Handle returns Unit and the host pushes the event record itself.
            return DotBoxDGenerationNames.TypeNames.GlobalSandboxType + ".Unit";
        }

        // A projection chain's Handle returns the projected value, so its return type is the full SandboxType the
        // projected CLR type maps to (scalar, Guid, enum, list, map, or DTO record). A projection whose type is
        // not marshaller-eligible yields no source and the chain fails safe (TryCreate returns null, no package).
        if (projectedTypeSymbol is not null && Rpc.SandboxTypeSourceEmitter.TryEmit(projectedTypeSymbol) is { } source)
        {
            return source;
        }

        throw new NotSupportedException();
    }

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
        bool hasLocalDecoder,
        ITypeSymbol? projectedTypeSymbol,
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
            // When the terminal projection is an anonymous type, neither the receiver (RemoteHookStage<TEvent, T>)
            // nor the handler (Func/Action<T, ...>) can spell T in C# source. Emit a GENERIC interceptor whose
            // arity matches the interceptable method's generic context (CS9177): EVERY receiver type argument
            // becomes a type parameter (reusing the receiver's own parameter names), and the receiver/handler/
            // return types reference those parameters. Roslyn infers them — including the anonymous one — at the
            // call site, so the emitted source never names the anonymous type.
            if (projectedTypeSymbol is INamedTypeSymbol { IsAnonymousType: true } anonymousProjection &&
                method.Parameters[0].Type is INamedTypeSymbol handlerType)
            {
                var typeParameters = receiverType.ConstructedFrom.TypeParameters;
                var substitution = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
                for (var i = 0; i < receiverType.TypeArguments.Length && i < typeParameters.Length; i++)
                {
                    substitution[receiverType.TypeArguments[i]] = typeParameters[i].Name;
                }

                return new HookChainInterception(
                    location.GetInterceptsLocationAttributeSyntax(),
                    RewriteWithTypeParameters(receiverType, substitution),
                    RewriteWithTypeParameters(handlerType, substitution),
                    RewriteWithTypeParameters((INamedTypeSymbol)method.ReturnType, substitution),
                    packageFullName,
                    installKind,
                    hasLocalDecoder,
                    hasLocalDecoder && substitution.TryGetValue(anonymousProjection, out var decoderTypeArgument)
                        ? decoderTypeArgument
                        : null,
                    string.Join(", ", typeParameters.Select(parameter => parameter.Name)));
            }

            return new HookChainInterception(
                location.GetInterceptsLocationAttributeSyntax(),
                receiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                packageFullName,
                installKind,
                hasLocalDecoder);
        }

        if (generatedRemoteKind is null)
        {
            return null;
        }

        // The generated-remote fallback spells the terminal element by its full type name, but an anonymous
        // projection has no nameable name (terminalElementTypeFullName would be the un-spellable "<anonymous
        // type ...>"). Only the known-stage branch above can emit a generic interceptor that lets Roslyn infer
        // it; decline here so no broken source is emitted (the real RunLocal then fails fast at the call site).
        if (projectedTypeSymbol is INamedTypeSymbol { IsAnonymousType: true })
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
            generatedRemoteKind.Value,
            hasLocalDecoder);
    }

    // The fully-qualified display of <paramref name="type"/> with any type (at any nesting depth) present in
    // <paramref name="substitution"/> replaced by its type-parameter name. Used to spell a generic interceptor's
    // receiver/handler/return when a type argument is an un-nameable anonymous type.
    private static string RewriteWithTypeParameters(ITypeSymbol type, Dictionary<ISymbol, string> substitution)
    {
        if (substitution.TryGetValue(type, out var parameterName))
        {
            return parameterName;
        }

        if (type is not INamedTypeSymbol { IsGenericType: true } named)
        {
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        var prefix = named.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : named.ContainingNamespace.ToDisplayString() + ".";
        var arguments = new List<string>(named.TypeArguments.Length);
        foreach (var argument in named.TypeArguments)
        {
            arguments.Add(RewriteWithTypeParameters(argument, substitution));
        }

        return DotBoxDGenerationNames.TypeNames.GlobalPrefix + prefix + named.Name +
            "<" + string.Join(", ", arguments) + ">";
    }

    private static string TerminalElementTypeFullName(
        IReadOnlyList<HookChainStage> stages,
        EquatableArray<EventPropertyModel> eventProperties,
        INamedTypeSymbol eventType,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        DotBoxDExpressionModel? projected = null;
        ITypeSymbol? projectedType = null;
        var terminalElementTypeFullName = eventType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        foreach (var stage in stages)
        {
            if (!stage.IsSelect)
            {
                continue;
            }

            var (elementParam, _, _) = LambdaParameters(stage.Lambda);
            if (elementParam is null || stage.Lambda.ExpressionBody is not { } body)
            {
                throw new NotSupportedException();
            }

            var scratchCapabilities = new SortedSet<string>(StringComparer.Ordinal);
            var scratchEffects = new SortedSet<string>(StringComparer.Ordinal);
            projected = DotBoxDExpressionModelFactory.Create(
                body,
                Context(elementParam, eventProperties, projected, projectedType, model, cancellationToken, scratchCapabilities, scratchEffects));
            var bodyTypeInfo = model.GetTypeInfo(body, cancellationToken);
            projectedType = bodyTypeInfo.ConvertedType ?? bodyTypeInfo.Type;
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
        ITypeSymbol? projectedType,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
        => projected is null
            ? new DotBoxDExpressionLoweringContext(
                elementParam, eventProperties, default, model, cancellationToken,
                capabilities: capabilities, effects: effects)
            : new DotBoxDExpressionLoweringContext(
                elementParam, eventProperties, default, model, cancellationToken,
                projectedElementName: elementParam,
                projectedElement: projected,
                projectedElementType: projectedType,
                capabilities: capabilities,
                effects: effects);

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

    // The leading lambda of a result terminal — Register(lambda, priority) / RegisterLocal(lambda, priority) —
    // where the (optional) trailing priority argument must not reject the chain.
    private static bool TryLeadingLambda(InvocationExpressionSyntax invocation, out LambdaExpressionSyntax lambda)
    {
        lambda = null!;
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count < 1 ||
            arguments[0].Expression is not LambdaExpressionSyntax lambdaExpression)
        {
            return false;
        }

        lambda = lambdaExpression;
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

internal enum HookChainReceiverKind
{
    Local,
    Remote
}

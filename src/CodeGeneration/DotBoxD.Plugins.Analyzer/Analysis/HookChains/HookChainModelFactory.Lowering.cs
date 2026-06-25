using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
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

    private static EquatableArray<string> ManifestEffects(
        HookChainInterceptorInstallKind installKind,
        DotBoxDStatementBodyModel shouldHandle,
        DotBoxDStatementBodyModel handleBody,
        ICollection<string> effects)
        => installKind == HookChainInterceptorInstallKind.LocalCallback
            ? DotBoxDManifestEffectModel.CreateLocalCallback(shouldHandle, handleBody, effects)
            : DotBoxDManifestEffectModel.Create(shouldHandle, handleBody, effects);

    private static DotBoxDStatementBodyModel LowerSendHandle(
        IReadOnlyList<HookChainStage> stages,
        LambdaExpressionSyntax terminalLambda,
        string terminalElementParam,
        string? terminalContextParam,
        ITypeSymbol? terminalContextType,
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
            terminalContextParam,
            terminalContextType,
            sendInvocation,
            eventProperties,
            model,
            cancellationToken,
            capabilities,
            effects);
        return DotBoxDHandleBodyModelFactory.FromSend(handle);
    }

    private static DotBoxDStatementBodyModel LowerRunHandle(
        IReadOnlyList<HookChainStage> stages,
        LambdaExpressionSyntax terminalLambda,
        string terminalElementParam,
        string? terminalContextParam,
        ITypeSymbol? terminalContextType,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        if (terminalContextParam is not null &&
            terminalLambda.ExpressionBody is InvocationExpressionSyntax sendInvocation &&
            DotBoxDHandleModelFactory.IsContextSend(sendInvocation.Expression, terminalContextParam))
        {
            return LowerSendHandle(
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
        }

        if (terminalLambda.ExpressionBody is not { } body)
        {
            throw new NotSupportedException();
        }

        var projection = HookChainStageLowerer.CreateProjection(
            stages,
            eventProperties,
            model,
            cancellationToken,
            capabilities,
            effects);
        var context = new DotBoxDExpressionLoweringContext(
            terminalElementParam,
            eventProperties,
            default,
            model,
            cancellationToken,
            projectedElementName: projection is null ? null : terminalElementParam,
            projectedElement: projection?.Value,
            projectedElementType: projection?.ValueType,
            serverContextParameterName: terminalContextParam,
            serverContextType: terminalContextType,
            capabilities: capabilities,
            effects: effects);
        if (terminalContextParam is not null &&
            body is InvocationExpressionSyntax helperInvocation &&
            DotBoxDKernelMethodInliner.TryInlineSendHandle(
                helperInvocation,
                terminalContextParam,
                context,
                part => DotBoxDExpressionModelFactory.Create(part, context)) is { } helperHandle)
        {
            return DotBoxDHandleBodyModelFactory.FromSend(helperHandle with { Prefix = projection?.Prefix });
        }

        var expression = DotBoxDExpressionModelFactory.Create(body, context);
        if (!string.Equals(expression.Type, DotBoxDGenerationNames.ManifestTypes.Unit, StringComparison.Ordinal))
        {
            throw new NotSupportedException();
        }

        return DotBoxDHandleBodyModelFactory.ReturnExpression(expression, projection?.Prefix);
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

            var (elementParam, contextParam) = LambdaParameters(stage.Lambda);
            if (elementParam is null || stage.Lambda.ExpressionBody is not { } body)
            {
                throw new NotSupportedException();
            }

            var scratchCapabilities = new SortedSet<string>(StringComparer.Ordinal);
            var scratchEffects = new SortedSet<string>(StringComparer.Ordinal);
            projected = DotBoxDExpressionModelFactory.Create(
                body,
                Context(
                    elementParam,
                    contextParam,
                    contextParam is null ? null : LambdaParameterType(stage.Lambda, contextParam, model, cancellationToken),
                    eventProperties,
                    projected,
                    projectedType,
                    model,
                    cancellationToken,
                    scratchCapabilities,
                    scratchEffects));
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
        string? contextParam,
        ITypeSymbol? contextType,
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
                serverContextParameterName: contextParam,
                serverContextType: contextType,
                capabilities: capabilities, effects: effects)
            : new DotBoxDExpressionLoweringContext(
                elementParam, eventProperties, default, model, cancellationToken,
                projectedElementName: elementParam,
                projectedElement: projected,
                projectedElementType: projectedType,
                serverContextParameterName: contextParam,
                serverContextType: contextType,
                capabilities: capabilities,
                effects: effects);

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

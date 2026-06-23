using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
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
            // return types reference those parameters. Roslyn infers them - including the anonymous one - at the
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
}

using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class ResultHookChain
{
    private static HookChainInterception? Interception(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax receiver,
        SemanticModel model,
        PluginKernelModel kernelModel,
        INamedTypeSymbol contextType,
        INamedTypeSymbol resultType,
        bool isLocal,
        bool hasServerContextParameter,
        bool isAsyncLocal,
        bool hasCancellationToken,
        bool receiverIsStage,
        GeneratedRemoteHookChainKind? generatedRemoteKind,
        string? generatedRemoteServerContextTypeFullName,
        CancellationToken cancellationToken)
    {
        var location = model.GetInterceptableLocation(invocation, cancellationToken);
        if (location is null)
        {
            return null;
        }

        var packageFullName = string.IsNullOrEmpty(kernelModel.Namespace)
            ? TypeNames.GlobalPrefix + kernelModel.PackageName
            : TypeNames.GlobalPrefix + kernelModel.Namespace + "." + kernelModel.PackageName;

        var contextFullName = contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var resultFullName = resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var serverContextFullName = ServerContextFullName(
            receiver,
            model,
            cancellationToken,
            generatedRemoteServerContextTypeFullName);
        var handlerFullName = ResultHandlerFullName(
            contextFullName,
            serverContextFullName,
            resultFullName,
            hasServerContextParameter,
            isAsyncLocal,
            hasCancellationToken);

        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method &&
            method.Parameters.Length >= 1 &&
            method.Parameters[0].Type is INamedTypeSymbol handlerType &&
            ResolvedReceiverType(method, model.GetTypeInfo(receiver, cancellationToken).Type) is { } resolvedReceiverType)
        {
            var resolvedReceiverFullName = resolvedReceiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return new HookChainInterception(
                location.GetInterceptsLocationAttributeSyntax(),
                resolvedReceiverFullName,
                handlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                packageFullName,
                isLocal ? HookChainInterceptorInstallKind.LocalResultChain : HookChainInterceptorInstallKind.ResultChain,
                ResultTypeFullName: resultFullName);
        }

        if (model.GetTypeInfo(receiver, cancellationToken).Type is not INamedTypeSymbol receiverType ||
            receiverType.TypeKind == TypeKind.Error)
        {
            return generatedRemoteKind is null
                ? null
                : GeneratedRemoteHookChainFallback.CreateResultInterception(
                    location.GetInterceptsLocationAttributeSyntax(),
                    contextFullName,
                    receiverIsStage,
                    resultFullName,
                    generatedRemoteServerContextTypeFullName,
                    hasServerContextParameter,
                    isAsyncLocal,
                    hasCancellationToken,
                    packageFullName,
                    isLocal ? HookChainInterceptorInstallKind.LocalResultChain : HookChainInterceptorInstallKind.ResultChain,
                    generatedRemoteKind.Value);
        }

        var receiverFullName = receiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new HookChainInterception(
            location.GetInterceptsLocationAttributeSyntax(),
            receiverFullName,
            handlerFullName,
            receiverFullName,
            packageFullName,
            isLocal ? HookChainInterceptorInstallKind.LocalResultChain : HookChainInterceptorInstallKind.ResultChain,
            ResultTypeFullName: resultFullName);
    }

    private static INamedTypeSymbol? ResolvedReceiverType(IMethodSymbol method, ITypeSymbol? expressionReceiverType)
        => method.ReceiverType is INamedTypeSymbol { TypeKind: not TypeKind.Error } receiverType
            ? receiverType
            : expressionReceiverType as INamedTypeSymbol;

    private static string ResultHandlerFullName(
        string eventFullName,
        string serverContextFullName,
        string resultFullName,
        bool hasServerContextParameter,
        bool isAsyncLocal,
        bool hasCancellationToken)
    {
        if (isAsyncLocal)
        {
            var token = TypeNames.GlobalCancellationToken;
            var result = TypeNames.GlobalValueTask + "<" + resultFullName + ">";
            if (hasServerContextParameter && hasCancellationToken)
            {
                return $"{TypeNames.GlobalFunc}<{eventFullName}, {serverContextFullName}, {token}, {result}>";
            }

            if (hasServerContextParameter)
            {
                return $"{TypeNames.GlobalFunc}<{eventFullName}, {serverContextFullName}, {result}>";
            }

            return hasCancellationToken
                ? $"{TypeNames.GlobalFunc}<{eventFullName}, {token}, {result}>"
                : $"{TypeNames.GlobalFunc}<{eventFullName}, {result}>";
        }

        if (!hasServerContextParameter)
        {
            return $"{TypeNames.GlobalFunc}<{eventFullName}, {resultFullName}>";
        }

        return $"{TypeNames.GlobalFunc}<{eventFullName}, {serverContextFullName}, {resultFullName}>";
    }

    private static string ServerContextFullName(
        ExpressionSyntax receiver,
        SemanticModel model,
        CancellationToken cancellationToken,
        string? generatedRemoteServerContextTypeFullName)
    {
        if (model.GetTypeInfo(receiver, cancellationToken).Type is not INamedTypeSymbol receiverType)
        {
            return generatedRemoteServerContextTypeFullName ?? TypeNames.GlobalHookContext;
        }

        var original = receiverType.OriginalDefinition.ToDisplayString();
        return original is DotBoxDGenerationNames.TypeNames.HookPipelineWithContextOriginal
            or DotBoxDGenerationNames.TypeNames.HookStageWithContextOriginal
            or DotBoxDGenerationNames.TypeNames.RemoteHookPipelineWithContextOriginal
            or DotBoxDGenerationNames.TypeNames.RemoteHookStageWithContextOriginal
            ? receiverType.TypeArguments[receiverType.TypeArguments.Length - 1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : TypeNames.GlobalHookContext;
    }
}

using DotBoxD.Plugins.Analyzer.Analysis.HookChains;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static partial class InvokeAsyncReceiverResolver
{
    private static bool TryResolveGeneratedServicesReceiver(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out string receiverType,
        out string? serverAccessType,
        out INamedTypeSymbol worldType)
    {
        receiverType = string.Empty;
        serverAccessType = null;
        worldType = null!;

        INamedTypeSymbol serverFacadeType;
        receiver = ResolveServicesReceiverExpression(model, receiver, cancellationToken);
        if (receiver is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Services",
                Expression: { } facadeExpression
            } ||
            model.GetTypeInfo(facadeExpression, cancellationToken).Type is not INamedTypeSymbol facadeType)
        {
            return false;
        }

        if (TryResolveWorld(facadeType, out worldType))
        {
            serverFacadeType = facadeType;
        }
        else if (!TryResolveGeneratedFacadeBase(facadeType, out serverFacadeType, out worldType))
        {
            return false;
        }

        receiverType = PluginServerInterfaceTypeName(worldType);
        serverAccessType = ServerInterfaceTypeName(serverFacadeType, worldType);
        return true;
    }

    private static ExpressionSyntax ResolveServicesReceiverExpression(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken)
    {
        receiver = HookChainAliasResolver.UnwrapTransparentExpression(receiver);
        for (var depth = 0; depth < 8; depth++)
        {
            if (HookChainAliasResolver.Initializer(receiver, model, cancellationToken) is not { } initializer)
            {
                return receiver;
            }

            receiver = HookChainAliasResolver.UnwrapTransparentExpression(initializer);
        }

        return receiver;
    }
}

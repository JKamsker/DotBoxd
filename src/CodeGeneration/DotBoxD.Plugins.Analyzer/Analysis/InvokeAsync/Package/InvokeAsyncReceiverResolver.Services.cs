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

        if (receiver is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Services",
                Expression: { } facadeExpression
            } ||
            model.GetTypeInfo(facadeExpression, cancellationToken).Type is not INamedTypeSymbol facadeType ||
            !TryResolveWorld(facadeType, out worldType))
        {
            return false;
        }

        receiverType = PluginServerInterfaceTypeName(worldType);
        serverAccessType = ServerInterfaceTypeName(facadeType, worldType);
        return true;
    }
}

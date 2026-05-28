using System.Threading;

namespace ShaRPC.SourceGenerator;

internal static class GeneratedTypeCollisionValidator
{
    public static ServiceResult Apply(ServiceResult result, ExistingTypeIndex existingTypes, CancellationToken ct)
    {
        if (result.Model is null)
        {
            return result;
        }

        var model = result.Model;
        var serviceName = NamingHelpers.StripInterfacePrefix(model.InterfaceName);

        var proxyName = serviceName + "Proxy";
        var proxy = existingTypes.Find(model.Namespace, proxyName, ct);
        if (proxy is not null)
        {
            return RejectedService(
                model,
                $"generated proxy type '{proxyName}' would collide with an existing type",
                proxy.Value.Location);
        }

        var dispatcherName = serviceName + "Dispatcher";
        var dispatcher = existingTypes.Find(model.Namespace, dispatcherName, ct);
        if (dispatcher is not null)
        {
            return RejectedService(
                model,
                $"generated dispatcher type '{dispatcherName}' would collide with an existing type",
                dispatcher.Value.Location);
        }

        if (NamingHelpers.CanGenerateAsyncSiblingInterface(model.InterfaceName))
        {
            var siblingName = NamingHelpers.AsyncSiblingInterfaceName(model.InterfaceName);
            var sibling = existingTypes.Find(model.Namespace, siblingName, ct);
            if (sibling is not null)
            {
                return RejectedService(
                    model,
                    $"generated async sibling interface '{siblingName}' would collide with an existing type",
                    sibling.Value.Location);
            }
        }

        var extensions = existingTypes.Find("ShaRPC.Generated", "ShaRpcGeneratedExtensions", ct);
        if (extensions is not null)
        {
            return RejectedService(
                model,
                "generated extension type 'ShaRPC.Generated.ShaRpcGeneratedExtensions' would collide with an existing type",
                extensions.Value.Location);
        }

        return result;
    }

    private static ServiceResult RejectedService(
        ServiceModel model,
        string reason,
        DiagnosticLocation location) =>
        new(
            Model: null,
            Error: null,
            MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
            MethodLocations: EquatableArray<DiagnosticLocation>.Empty,
            ServiceLocation: location,
            QualifiedInterfaceName: IdentifierHelpers.QualifyTypeName(model.Namespace, model.InterfaceName),
            ServiceDiagnostic: new ServiceDiagnostic(GetDisplayName(model), reason, location));

    private static string GetDisplayName(ServiceModel model) =>
        string.IsNullOrEmpty(model.Namespace)
            ? IdentifierHelpers.EscapeIdentifier(model.InterfaceName)
            : IdentifierHelpers.EscapeNamespace(model.Namespace) + "." +
                IdentifierHelpers.EscapeIdentifier(model.InterfaceName);
}

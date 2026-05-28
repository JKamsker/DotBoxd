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
        var proxy = new ExistingTypeKey(model.Namespace, proxyName);
        if (existingTypes.Contains(proxy, ct))
        {
            return RejectedService(
                model,
                $"generated proxy type '{proxyName}' would collide with an existing type",
                proxy);
        }

        var dispatcherName = serviceName + "Dispatcher";
        var dispatcher = new ExistingTypeKey(model.Namespace, dispatcherName);
        if (existingTypes.Contains(dispatcher, ct))
        {
            return RejectedService(
                model,
                $"generated dispatcher type '{dispatcherName}' would collide with an existing type",
                dispatcher);
        }

        if (NamingHelpers.CanGenerateAsyncSiblingInterface(model.InterfaceName))
        {
            var siblingName = NamingHelpers.AsyncSiblingInterfaceName(model.InterfaceName);
            var sibling = new ExistingTypeKey(model.Namespace, siblingName);
            if (existingTypes.Contains(sibling, ct) && WillGenerateAsyncSiblingInterface(model, ct))
            {
                return RejectedService(
                    model,
                    $"generated async sibling interface '{siblingName}' would collide with an existing type",
                    sibling);
            }
        }

        var extensions = new ExistingTypeKey("ShaRPC.Generated", "ShaRpcGeneratedExtensions");
        if (existingTypes.Contains(extensions, ct))
        {
            return RejectedService(
                model,
                "generated extension type 'ShaRPC.Generated.ShaRpcGeneratedExtensions' would collide with an existing type",
                extensions);
        }

        return result;
    }

    private static bool WillGenerateAsyncSiblingInterface(ServiceModel model, CancellationToken ct)
    {
        if (!NamingHelpers.CanGenerateAsyncSiblingInterface(model.InterfaceName))
        {
            return false;
        }

        var (siblings, _) = AsyncSiblingProjector.Compute(model, ct);
        return !siblings.IsEmpty;
    }

    private static ServiceResult RejectedService(
        ServiceModel model,
        string reason,
        ExistingTypeKey existingType) =>
        new(
            Model: null,
            Error: null,
            MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
            MethodLocations: EquatableArray<DiagnosticLocation>.Empty,
            ServiceLocation: default,
            QualifiedInterfaceName: IdentifierHelpers.QualifyTypeName(model.Namespace, model.InterfaceName),
            ServiceDiagnostic: null,
            ExistingTypeCollision: new ExistingTypeCollisionDiagnostic(
                GetDisplayName(model),
                reason,
                existingType));

    private static string GetDisplayName(ServiceModel model) =>
        string.IsNullOrEmpty(model.Namespace)
            ? IdentifierHelpers.EscapeIdentifier(model.InterfaceName)
            : IdentifierHelpers.EscapeNamespace(model.Namespace) + "." +
                IdentifierHelpers.EscapeIdentifier(model.InterfaceName);
}

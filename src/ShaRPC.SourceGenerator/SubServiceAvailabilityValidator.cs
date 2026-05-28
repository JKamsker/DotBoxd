using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal sealed record RejectedServiceIndex(EquatableArray<string> QualifiedInterfaceNames)
{
    public static RejectedServiceIndex Create(ImmutableArray<ServiceResult> results, CancellationToken ct)
    {
        var names = ImmutableArray.CreateBuilder<string>();
        foreach (var result in results)
        {
            ct.ThrowIfCancellationRequested();

            if (result.Model is null &&
                result.ServiceDiagnostic is not null &&
                !string.IsNullOrEmpty(result.QualifiedInterfaceName))
            {
                names.Add(result.QualifiedInterfaceName);
            }
        }

        return new RejectedServiceIndex(names.ToImmutable().ToEquatableArray());
    }

    public bool Contains(string qualifiedInterfaceName, CancellationToken ct)
    {
        foreach (var name in QualifiedInterfaceNames.Array)
        {
            ct.ThrowIfCancellationRequested();

            if (name == qualifiedInterfaceName)
            {
                return true;
            }
        }

        return false;
    }
}

internal static class SubServiceAvailabilityValidator
{
    public static ServiceResult Apply(
        ServiceResult result,
        RejectedServiceIndex rejectedServices,
        CancellationToken ct)
    {
        if (result.Model is null)
        {
            return result;
        }

        var methods = new List<MethodModel>();
        var diagnostics = new List<MethodDiagnostic>(result.MethodDiagnostics.Array);
        var changed = false;
        for (var i = 0; i < result.Model.Methods.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var method = result.Model.Methods[i];
            if (method.UnsupportedReason is null &&
                method.SubService is not null &&
                rejectedServices.Contains(method.SubService.QualifiedInterfaceName, ct))
            {
                var reason =
                    $"sub-service return type '{method.SubService.QualifiedInterfaceName}' cannot be proxied because that service was not generated";
                method = method with { UnsupportedReason = reason };
                diagnostics.Add(new MethodDiagnostic(
                    GetDisplayName(result.Model),
                    method.Name,
                    reason,
                    GetLocation(result.MethodLocations, i)));
                changed = true;
            }

            methods.Add(method);
        }

        if (!changed)
        {
            return result;
        }

        return result with
        {
            Model = result.Model with { Methods = methods.ToEquatableArray() },
            MethodDiagnostics = diagnostics.ToEquatableArray(),
        };
    }

    private static DiagnosticLocation GetLocation(
        EquatableArray<DiagnosticLocation> locations,
        int index)
    {
        if (index < 0 || index >= locations.Count)
        {
            return default;
        }

        return locations[index];
    }

    private static string GetDisplayName(ServiceModel model) =>
        string.IsNullOrEmpty(model.Namespace)
            ? IdentifierHelpers.EscapeIdentifier(model.InterfaceName)
            : IdentifierHelpers.EscapeNamespace(model.Namespace) + "." +
                IdentifierHelpers.EscapeIdentifier(model.InterfaceName);
}

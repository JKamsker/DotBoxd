using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal sealed record RejectedServiceIndex(EquatableArray<string> QualifiedInterfaceNames)
{
    public static RejectedServiceIndex Create(ImmutableArray<RejectedServiceIdentity> services, CancellationToken ct)
    {
        var names = new List<string>(services.Length);
        foreach (var service in services)
        {
            ct.ThrowIfCancellationRequested();
            names.Add(service.QualifiedInterfaceName);
        }

        names.Sort((left, right) =>
        {
            ct.ThrowIfCancellationRequested();
            return string.Compare(left, right, System.StringComparison.Ordinal);
        });

        var uniqueNames = new List<string>(names.Count);
        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();
            if (uniqueNames.Count == 0 || uniqueNames[uniqueNames.Count - 1] != name)
            {
                uniqueNames.Add(name);
            }
        }

        return new RejectedServiceIndex(uniqueNames.ToEquatableArray());
    }

    public bool Contains(string qualifiedInterfaceName, CancellationToken ct)
    {
        var low = 0;
        var high = QualifiedInterfaceNames.Count - 1;
        while (low <= high)
        {
            ct.ThrowIfCancellationRequested();

            var mid = low + ((high - low) / 2);
            var comparison = string.Compare(
                QualifiedInterfaceNames[mid],
                qualifiedInterfaceName,
                System.StringComparison.Ordinal);
            if (comparison == 0)
            {
                return true;
            }

            if (comparison < 0)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
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

        for (var i = 0; i < result.Model.Properties.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var property = result.Model.Properties[i];
            if (property.SubService is not null &&
                rejectedServices.Contains(property.SubService.QualifiedInterfaceName, ct))
            {
                var reason =
                    $"sub-service property '{IdentifierHelpers.UnescapeIdentifier(property.Name)}' cannot be proxied because that service was not generated";
                return result with
                {
                    Model = null,
                    MethodDiagnostics = EquatableArray<MethodDiagnostic>.Empty,
                    MethodLocations = EquatableArray<DiagnosticLocation>.Empty,
                    PropertyLocations = EquatableArray<DiagnosticLocation>.Empty,
                    ServiceDiagnostic = new ServiceDiagnostic(
                        GetDisplayName(result.Model),
                        reason,
                        GetLocation(result.PropertyLocations, i)),
                };
            }
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

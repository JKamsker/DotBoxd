using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static class ServiceAvailabilityIndexHelpers
{
    public static List<string> SortedUnique(List<string> names, CancellationToken ct)
    {
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

        return uniqueNames;
    }

    public static bool ContainsSorted(
        EquatableArray<string> qualifiedInterfaceNames,
        string qualifiedInterfaceName,
        CancellationToken ct)
    {
        var low = 0;
        var high = qualifiedInterfaceNames.Count - 1;
        while (low <= high)
        {
            ct.ThrowIfCancellationRequested();

            var mid = low + ((high - low) / 2);
            var comparison = string.Compare(
                qualifiedInterfaceNames[mid],
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

        return new RejectedServiceIndex(ServiceAvailabilityIndexHelpers.SortedUnique(names, ct).ToEquatableArray());
    }

    public bool Contains(string qualifiedInterfaceName, CancellationToken ct)
        => ServiceAvailabilityIndexHelpers.ContainsSorted(QualifiedInterfaceNames, qualifiedInterfaceName, ct);
}

internal static class SubServiceAvailabilityValidator
{
    public static ServiceResult Apply(
        ServiceResult result,
        GeneratedServiceIndex generatedServices,
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
                IsUnavailable(property.SubService, generatedServices, rejectedServices, ct))
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
                IsUnavailable(method.SubService, generatedServices, rejectedServices, ct))
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
            else if (method.UnsupportedReason is null &&
                method.SubService is not null &&
                generatedServices.TryGetInstanceScopedSubServiceProperty(
                    method.SubService.QualifiedInterfaceName,
                    ct,
                    out var propertyName))
            {
                var reason =
                    $"sub-service return type '{method.SubService.QualifiedInterfaceName}' exposes sub-service property '{IdentifierHelpers.UnescapeIdentifier(propertyName)}' whose proxy would not be instance-scoped";
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

    private static bool IsUnavailable(
        SubServiceInfo subService,
        GeneratedServiceIndex generatedServices,
        RejectedServiceIndex rejectedServices,
        CancellationToken ct)
        => rejectedServices.Contains(subService.QualifiedInterfaceName, ct) ||
            (!subService.HasProxyCompanion &&
             !generatedServices.Contains(subService.QualifiedInterfaceName, ct));
}

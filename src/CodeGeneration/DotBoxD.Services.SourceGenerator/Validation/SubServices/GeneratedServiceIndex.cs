using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal sealed record GeneratedServiceIndex(
    EquatableArray<string> QualifiedInterfaceNames,
    EquatableArray<InstanceScopedSubServiceProperty> InstanceScopedSubServiceProperties)
{
    public static GeneratedServiceIndex Create(ImmutableArray<GeneratedServiceAvailability> services, CancellationToken ct)
    {
        var names = new List<string>(services.Length);
        var instanceScopedProperties = new List<InstanceScopedSubServiceProperty>();
        foreach (var service in services)
        {
            ct.ThrowIfCancellationRequested();
            names.Add(service.QualifiedInterfaceName);
            foreach (var propertyName in service.InstanceScopedSubServicePropertyNames)
            {
                ct.ThrowIfCancellationRequested();
                instanceScopedProperties.Add(new InstanceScopedSubServiceProperty(
                    service.QualifiedInterfaceName,
                    propertyName));
            }
        }

        return new GeneratedServiceIndex(
            ServiceAvailabilityIndexHelpers.SortedUnique(names, ct).ToEquatableArray(),
            SortUnique(instanceScopedProperties, ct).ToEquatableArray());
    }

    public bool Contains(string qualifiedInterfaceName, CancellationToken ct)
        => ServiceAvailabilityIndexHelpers.ContainsSorted(QualifiedInterfaceNames, qualifiedInterfaceName, ct);

    public bool TryGetInstanceScopedSubServiceProperty(
        string qualifiedInterfaceName,
        CancellationToken ct,
        out string propertyName)
    {
        foreach (var property in InstanceScopedSubServiceProperties)
        {
            ct.ThrowIfCancellationRequested();
            var comparison = string.Compare(
                property.QualifiedInterfaceName,
                qualifiedInterfaceName,
                System.StringComparison.Ordinal);
            if (comparison > 0)
            {
                break;
            }

            if (comparison == 0)
            {
                propertyName = property.PropertyName;
                return true;
            }
        }

        propertyName = string.Empty;
        return false;
    }

    private static List<InstanceScopedSubServiceProperty> SortUnique(
        List<InstanceScopedSubServiceProperty> properties,
        CancellationToken ct)
    {
        properties.Sort((left, right) =>
        {
            ct.ThrowIfCancellationRequested();
            var serviceComparison = string.Compare(
                left.QualifiedInterfaceName,
                right.QualifiedInterfaceName,
                System.StringComparison.Ordinal);
            return serviceComparison != 0
                ? serviceComparison
                : string.Compare(left.PropertyName, right.PropertyName, System.StringComparison.Ordinal);
        });

        var uniqueProperties = new List<InstanceScopedSubServiceProperty>(properties.Count);
        foreach (var property in properties)
        {
            ct.ThrowIfCancellationRequested();
            if (uniqueProperties.Count == 0 ||
                !uniqueProperties[uniqueProperties.Count - 1].Equals(property))
            {
                uniqueProperties.Add(property);
            }
        }

        return uniqueProperties;
    }
}

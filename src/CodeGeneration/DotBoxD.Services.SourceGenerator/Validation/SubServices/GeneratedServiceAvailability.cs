using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal readonly record struct GeneratedServiceAvailability(
    string QualifiedInterfaceName,
    EquatableArray<string> InstanceScopedSubServicePropertyNames)
{
    public static GeneratedServiceAvailability From(ServiceResult result, CancellationToken ct)
    {
        var propertyNames = new List<string>();
        foreach (var property in result.Model!.Properties)
        {
            ct.ThrowIfCancellationRequested();
            if (property.SubService is not null)
            {
                propertyNames.Add(property.Name);
            }
        }

        return new GeneratedServiceAvailability(
            result.QualifiedInterfaceName,
            ServiceAvailabilityIndexHelpers.SortedUnique(propertyNames, ct).ToEquatableArray());
    }
}

internal readonly record struct InstanceScopedSubServiceProperty(
    string QualifiedInterfaceName,
    string PropertyName);

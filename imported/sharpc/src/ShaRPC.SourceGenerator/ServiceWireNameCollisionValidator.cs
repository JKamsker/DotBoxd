using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal sealed record ServiceWireNameIndex(EquatableArray<ServiceWireNameEntry> DuplicateEntries)
{
    public static ServiceWireNameIndex Create(ImmutableArray<ServiceIdentity> services, CancellationToken ct)
    {
        var entries = ImmutableArray.CreateBuilder<ServiceWireNameEntry>();
        foreach (var service in services)
        {
            ct.ThrowIfCancellationRequested();

            entries.Add(new ServiceWireNameEntry(
                service.ServiceName,
                service.QualifiedInterfaceName));
        }

        return new ServiceWireNameIndex(GetDuplicates(entries.ToImmutable(), ct));
    }

    public ServiceWireNameEntry? FindCollision(
        ServiceModel model,
        string qualifiedInterfaceName,
        CancellationToken ct)
    {
        var target = new ServiceWireNameEntry(model.ServiceName, qualifiedInterfaceName);
        var start = LowerBound(target, ct);
        for (var i = start; i < DuplicateEntries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var entry = DuplicateEntries[i];
            if (string.Compare(entry.ServiceName, model.ServiceName, System.StringComparison.Ordinal) != 0)
            {
                break;
            }

            if (entry.QualifiedInterfaceName != qualifiedInterfaceName)
            {
                return entry;
            }
        }

        return null;
    }

    private static EquatableArray<ServiceWireNameEntry> GetDuplicates(
        ImmutableArray<ServiceWireNameEntry> entries,
        CancellationToken ct)
    {
        var ordered = new List<ServiceWireNameEntry>(entries);
        ordered.Sort((left, right) =>
        {
            ct.ThrowIfCancellationRequested();
            return ServiceWireNameEntryComparer.Instance.Compare(left, right);
        });

        var duplicates = new List<ServiceWireNameEntry>();
        for (var i = 0; i < ordered.Count;)
        {
            ct.ThrowIfCancellationRequested();

            var start = i;
            i++;
            while (i < ordered.Count && ordered[i].ServiceName == ordered[start].ServiceName)
            {
                ct.ThrowIfCancellationRequested();
                i++;
            }

            if (i - start > 1)
            {
                for (var j = start; j < i; j++)
                {
                    duplicates.Add(ordered[j]);
                }
            }
        }

        return duplicates.ToEquatableArray();
    }

    private int LowerBound(ServiceWireNameEntry target, CancellationToken ct)
    {
        var low = 0;
        var high = DuplicateEntries.Count;
        while (low < high)
        {
            ct.ThrowIfCancellationRequested();

            var mid = low + ((high - low) / 2);
            if (string.Compare(DuplicateEntries[mid].ServiceName, target.ServiceName, System.StringComparison.Ordinal) < 0)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}

internal readonly record struct ServiceWireNameEntry(
    string ServiceName,
    string QualifiedInterfaceName);

internal sealed class ServiceWireNameEntryComparer : IComparer<ServiceWireNameEntry>
{
    public static ServiceWireNameEntryComparer Instance { get; } = new();

    public int Compare(ServiceWireNameEntry left, ServiceWireNameEntry right)
    {
        var serviceName = string.Compare(left.ServiceName, right.ServiceName, System.StringComparison.Ordinal);
        return serviceName != 0
            ? serviceName
            : string.Compare(left.QualifiedInterfaceName, right.QualifiedInterfaceName, System.StringComparison.Ordinal);
    }
}

internal static class ServiceWireNameCollisionValidator
{
    public static ServiceResult Apply(
        ServiceResult result,
        ServiceWireNameIndex index,
        CancellationToken ct)
    {
        if (result.Model is null)
        {
            return result;
        }

        var collision = index.FindCollision(result.Model, result.QualifiedInterfaceName, ct);
        if (collision is null)
        {
            return result;
        }

        var reason =
            $"wire service name '{result.Model.RawServiceName}' is used by multiple services; give each service a distinct [ShaRpcService(Name = ...)] value";

        return new ServiceResult(
            Model: null,
            Error: null,
            MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
            MethodLocations: EquatableArray<DiagnosticLocation>.Empty,
            ServiceLocation: result.ServiceLocation,
            QualifiedInterfaceName: result.QualifiedInterfaceName,
            ServiceDiagnostic: new ServiceDiagnostic(
                GetDisplayName(result.Model),
                reason,
                result.ServiceLocation));
    }

    private static string GetDisplayName(ServiceModel model) =>
        string.IsNullOrEmpty(model.Namespace)
            ? IdentifierHelpers.EscapeIdentifier(model.InterfaceName)
            : IdentifierHelpers.EscapeNamespace(model.Namespace) + "." +
                IdentifierHelpers.EscapeIdentifier(model.InterfaceName);
}

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal sealed record GeneratedServiceNameIndex(EquatableArray<GeneratedServiceNameEntry> DuplicateEntries)
{
    public static GeneratedServiceNameIndex Create(
        ImmutableArray<GeneratedServiceIdentity> services,
        CancellationToken ct)
    {
        var entries = ImmutableArray.CreateBuilder<GeneratedServiceNameEntry>();
        foreach (var service in services)
        {
            ct.ThrowIfCancellationRequested();

            entries.Add(new GeneratedServiceNameEntry(
                service.Namespace,
                service.InterfaceName,
                NamingHelpers.StripInterfacePrefix(service.InterfaceName)));
        }

        return new GeneratedServiceNameIndex(GetDuplicates(entries.ToImmutable(), ct));
    }

    public GeneratedServiceNameEntry? FindCollision(ServiceModel model, CancellationToken ct)
    {
        var target = new GeneratedServiceNameEntry(
            model.Namespace,
            model.InterfaceName,
            NamingHelpers.StripInterfacePrefix(model.InterfaceName));
        var start = LowerBound(target, ct);
        for (var i = start; i < DuplicateEntries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var entry = DuplicateEntries[i];
            if (GeneratedServiceNameEntryComparer.CompareKey(entry, target) != 0)
            {
                break;
            }

            if (entry.InterfaceName != model.InterfaceName)
            {
                return entry;
            }
        }

        return null;
    }

    private static EquatableArray<GeneratedServiceNameEntry> GetDuplicates(
        ImmutableArray<GeneratedServiceNameEntry> entries,
        CancellationToken ct)
    {
        var ordered = new List<GeneratedServiceNameEntry>(entries);
        ordered.Sort((left, right) =>
        {
            ct.ThrowIfCancellationRequested();
            return GeneratedServiceNameEntryComparer.Instance.Compare(left, right);
        });

        var duplicates = new List<GeneratedServiceNameEntry>();
        for (var i = 0; i < ordered.Count;)
        {
            ct.ThrowIfCancellationRequested();

            var start = i;
            i++;
            while (i < ordered.Count &&
                GeneratedServiceNameEntryComparer.CompareKey(ordered[start], ordered[i]) == 0)
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

    private int LowerBound(GeneratedServiceNameEntry target, CancellationToken ct)
    {
        var low = 0;
        var high = DuplicateEntries.Count;
        while (low < high)
        {
            ct.ThrowIfCancellationRequested();

            var mid = low + ((high - low) / 2);
            if (GeneratedServiceNameEntryComparer.CompareKey(DuplicateEntries[mid], target) < 0)
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

internal readonly record struct GeneratedServiceNameEntry(
    string Namespace,
    string InterfaceName,
    string GeneratedName);

internal sealed class GeneratedServiceNameEntryComparer : IComparer<GeneratedServiceNameEntry>
{
    public static GeneratedServiceNameEntryComparer Instance { get; } = new();

    public int Compare(GeneratedServiceNameEntry left, GeneratedServiceNameEntry right)
    {
        var key = CompareKey(left, right);
        return key != 0
            ? key
            : string.Compare(left.InterfaceName, right.InterfaceName, System.StringComparison.Ordinal);
    }

    public static int CompareKey(GeneratedServiceNameEntry left, GeneratedServiceNameEntry right)
    {
        var ns = string.Compare(left.Namespace, right.Namespace, System.StringComparison.Ordinal);
        return ns != 0
            ? ns
            : string.Compare(left.GeneratedName, right.GeneratedName, System.StringComparison.Ordinal);
    }
}

internal static class GeneratedServiceCollisionValidator
{
    public static ServiceResult Apply(
        ServiceResult result,
        GeneratedServiceNameIndex index,
        CancellationToken ct)
    {
        if (result.Model is null)
        {
            return result;
        }

        var collision = index.FindCollision(result.Model, ct);
        if (collision is null)
        {
            return result;
        }

        var generatedName = NamingHelpers.StripInterfacePrefix(result.Model.InterfaceName);
        var otherService = GetDisplayName(collision.Value.Namespace, collision.Value.InterfaceName);
        var reason =
            $"generated proxy and dispatcher type names '{generatedName}Proxy' and '{generatedName}Dispatcher' would collide with service '{otherService}'";

        return new ServiceResult(
            Model: null,
            Error: null,
            MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
            MethodLocations: EquatableArray<DiagnosticLocation>.Empty,
            ServiceLocation: result.ServiceLocation,
            QualifiedInterfaceName: result.QualifiedInterfaceName,
            ServiceDiagnostic: new ServiceDiagnostic(
                GetDisplayName(result.Model.Namespace, result.Model.InterfaceName),
                reason,
                result.ServiceLocation));
    }

    private static string GetDisplayName(string @namespace, string interfaceName) =>
        string.IsNullOrEmpty(@namespace)
            ? IdentifierHelpers.EscapeIdentifier(interfaceName)
            : IdentifierHelpers.EscapeNamespace(@namespace) + "." +
                IdentifierHelpers.EscapeIdentifier(interfaceName);
}

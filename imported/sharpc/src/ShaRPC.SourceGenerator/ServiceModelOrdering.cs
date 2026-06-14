using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal static class ServiceModelOrdering
{
    public static EquatableArray<ServiceModel> Sort(
        ImmutableArray<ServiceModel> services,
        CancellationToken ct)
    {
        var ordered = new List<ServiceModel>(services);
        ordered.Sort((left, right) =>
        {
            ct.ThrowIfCancellationRequested();

            var ns = string.Compare(left.Namespace, right.Namespace, StringComparison.Ordinal);
            if (ns != 0)
            {
                return ns;
            }

            var interfaceName = string.Compare(left.InterfaceName, right.InterfaceName, StringComparison.Ordinal);
            return interfaceName != 0
                ? interfaceName
                : string.Compare(left.ServiceName, right.ServiceName, StringComparison.Ordinal);
        });

        return ordered.ToEquatableArray();
    }

    public static EquatableArray<ServiceIdentity> SortIdentities(
        ImmutableArray<ServiceIdentity> services,
        CancellationToken ct)
    {
        var ordered = new List<ServiceIdentity>(services);
        ordered.Sort((left, right) => CompareIdentity(left, right, ct));

        return ordered.ToEquatableArray();
    }

    private static int CompareIdentity(
        ServiceIdentity left,
        ServiceIdentity right,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var ns = string.Compare(left.Namespace, right.Namespace, StringComparison.Ordinal);
        if (ns != 0)
        {
            return ns;
        }

        var interfaceName = string.Compare(left.InterfaceName, right.InterfaceName, StringComparison.Ordinal);
        return interfaceName != 0
            ? interfaceName
            : string.Compare(left.ServiceName, right.ServiceName, StringComparison.Ordinal);
    }
}

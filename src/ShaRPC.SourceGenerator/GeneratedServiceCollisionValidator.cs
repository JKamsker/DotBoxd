using System.Collections.Immutable;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal sealed record GeneratedServiceNameIndex(EquatableArray<GeneratedServiceNameEntry> Entries)
{
    public static GeneratedServiceNameIndex Create(
        ImmutableArray<ServiceResult> results,
        CancellationToken ct)
    {
        var entries = ImmutableArray.CreateBuilder<GeneratedServiceNameEntry>();
        foreach (var result in results)
        {
            ct.ThrowIfCancellationRequested();

            if (result.Model is null)
            {
                continue;
            }

            entries.Add(new GeneratedServiceNameEntry(
                result.Model.Namespace,
                result.Model.InterfaceName,
                NamingHelpers.StripInterfacePrefix(result.Model.InterfaceName),
                result.ServiceLocation));
        }

        return new GeneratedServiceNameIndex(entries.ToImmutable().ToEquatableArray());
    }

    public GeneratedServiceNameEntry? FindCollision(ServiceModel model, CancellationToken ct)
    {
        var generatedName = NamingHelpers.StripInterfacePrefix(model.InterfaceName);
        foreach (var entry in Entries.Array)
        {
            ct.ThrowIfCancellationRequested();

            if (entry.Namespace == model.Namespace &&
                entry.GeneratedName == generatedName &&
                entry.InterfaceName != model.InterfaceName)
            {
                return entry;
            }
        }

        return null;
    }
}

internal readonly record struct GeneratedServiceNameEntry(
    string Namespace,
    string InterfaceName,
    string GeneratedName,
    DiagnosticLocation Location);

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

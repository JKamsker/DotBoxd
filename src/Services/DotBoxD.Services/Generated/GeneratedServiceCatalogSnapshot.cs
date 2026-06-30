namespace DotBoxD.Services.Generated;

internal static class GeneratedServiceCatalogSnapshot
{
    public static IReadOnlyList<GeneratedService> Snapshot(IReadOnlyList<GeneratedService> services)
    {
        var snapshot = new GeneratedService[services.Count];
        for (var i = 0; i < services.Count; i++)
        {
            snapshot[i] = Snapshot(services[i]);
        }

        return snapshot;
    }

    public static GeneratedService Snapshot(GeneratedService service)
        => service with { Methods = SnapshotMethods(service.Methods) };

    private static IReadOnlyList<GeneratedMethod> SnapshotMethods(IReadOnlyList<GeneratedMethod>? methods)
    {
        if (methods is null || methods.Count == 0)
        {
            return Array.Empty<GeneratedMethod>();
        }

        var snapshot = new GeneratedMethod[methods.Count];
        for (var i = 0; i < methods.Count; i++)
        {
            var method = methods[i];
            snapshot[i] = method with { Parameters = SnapshotParameters(method.Parameters) };
        }

        return snapshot;
    }

    private static IReadOnlyList<GeneratedParameter> SnapshotParameters(IReadOnlyList<GeneratedParameter>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return Array.Empty<GeneratedParameter>();
        }

        var snapshot = new GeneratedParameter[parameters.Count];
        for (var i = 0; i < parameters.Count; i++)
        {
            snapshot[i] = parameters[i];
        }

        return snapshot;
    }
}

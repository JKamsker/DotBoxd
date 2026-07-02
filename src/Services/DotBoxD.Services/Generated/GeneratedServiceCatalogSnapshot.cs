namespace DotBoxD.Services.Generated;

internal static class GeneratedServiceCatalogSnapshot
{
    public static IReadOnlyList<GeneratedService> Snapshot(IReadOnlyList<GeneratedService> services)
        => Snapshot(services, validateImplementationTypes: true);

    public static IReadOnlyList<GeneratedService> Snapshot(
        IReadOnlyList<GeneratedService> services,
        bool validateImplementationTypes)
    {
        var snapshot = new GeneratedService[services.Count];
        for (var i = 0; i < services.Count; i++)
        {
            GeneratedServiceMetadataValidator.Validate(services[i], nameof(services), validateImplementationTypes);
            snapshot[i] = SnapshotCore(services[i]);
        }

        return Array.AsReadOnly(snapshot);
    }

    public static GeneratedService Snapshot(GeneratedService service)
    {
        GeneratedServiceMetadataValidator.Validate(service, nameof(service));
        return SnapshotCore(service);
    }

    private static GeneratedService SnapshotCore(GeneratedService service)
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

        return Array.AsReadOnly(snapshot);
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

        return Array.AsReadOnly(snapshot);
    }
}

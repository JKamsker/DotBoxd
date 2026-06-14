namespace DotBoxd.Plugins;

using DotBoxd.Kernels.Serialization.Json;

/// <summary>
/// Exposes the versioned, machine-readable JSON Schema artifact that describes the plugin package
/// envelope accepted by <see cref="PluginPackageJsonSerializer.Import(string)"/>. The schema is
/// checked into <c>schemas/v1/</c> and embedded into this assembly so consumers (admin UIs, upload
/// validators, plugin authors) can validate JSON before sending it to a server. The module envelope
/// and the shared <see cref="DotBoxdJsonSchemas.SchemaVersion"/> live in the purpose-agnostic
/// <c>DotBoxd.Kernels.Serialization.Json</c> package.
/// </summary>
public static class PluginPackageJsonSchemas
{
    private const string PluginPackageResourceName =
        "DotBoxd.Plugins.schemas.v1.dotboxd-plugin-package.schema.json";

    /// <summary>
    /// Version of the JSON ingestion schema contract. Re-exposes
    /// <see cref="DotBoxdJsonSchemas.SchemaVersion"/> so the module and plugin-package schemas cannot drift.
    /// </summary>
    public static string SchemaVersion => DotBoxdJsonSchemas.SchemaVersion;

    /// <summary>
    /// JSON Schema document for the plugin package envelope
    /// (<see cref="PluginPackageJsonSerializer.Import(string)"/>).
    /// </summary>
    public static string PackageEnvelope => ReadResource(PluginPackageResourceName);

    private static string ReadResource(string resourceName)
    {
        var assembly = typeof(PluginPackageJsonSchemas).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded JSON schema resource '{resourceName}' was not found. " +
                "Ensure the schemas/v1 plugin-package artifact is embedded by DotBoxd.Plugins.csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

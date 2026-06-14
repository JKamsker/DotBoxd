namespace DotBoxd.Kernels.Serialization.Json;

/// <summary>
/// Exposes the versioned, machine-readable JSON Schema artifact that describes the public
/// Safe IR module ingestion envelope accepted by <see cref="DotBoxdJsonImporter.Import(string)"/>.
/// The plugin package envelope schema lives with the plugin layer in
/// <c>DotBoxd.Plugins.PluginPackageJsonSchemas</c>.
/// </summary>
/// <remarks>
/// The schema is checked into <c>schemas/v1/</c> and embedded into this assembly so consumers
/// (admin UIs, upload validators, package tooling) can validate JSON before sending it to a server
/// without inferring the contract from importer source. The schema is kept in sync with the
/// importer's strict shape by the CMP-0012 drift test. When the JSON contract changes, bump
/// <see cref="SchemaVersion"/> and the <c>v{n}</c> directory segment, and update the schema files
/// alongside the importer/exporter.
/// </remarks>
public static class DotBoxdJsonSchemas
{
    private const string ResourcePrefix = "DotBoxd.Kernels.Serialization.Json.schemas.v1.";

    private const string ModuleResourceName = ResourcePrefix + "dotboxd-kernel-module.schema.json";

    /// <summary>
    /// Version of the JSON ingestion schema contract. Matches the <c>v1</c> directory segment and
    /// the <c>x-dotboxd-schema-version</c> field embedded in each schema document.
    /// </summary>
    public static string SchemaVersion => "1.0.0";

    /// <summary>
    /// JSON Schema document for the Safe IR module envelope
    /// (<see cref="DotBoxdJsonImporter.Import(string)"/>).
    /// </summary>
    public static string ModuleEnvelope => ReadResource(ModuleResourceName);

    private static string ReadResource(string resourceName)
    {
        var assembly = typeof(DotBoxdJsonSchemas).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded JSON schema resource '{resourceName}' was not found. " +
                "Ensure the schemas/v1 artifacts are embedded by DotBoxd.Kernels.Serialization.Json.csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

namespace SafeIR.Serialization.Json;

/// <summary>
/// Exposes the versioned, machine-readable JSON Schema artifact that describes the public
/// Safe IR module ingestion envelope accepted by <see cref="SafeIrJsonImporter.Import(string)"/>.
/// The plugin package envelope schema lives with the plugin layer in
/// <c>SafeIR.Plugins.PluginPackageJsonSchemas</c>.
/// </summary>
/// <remarks>
/// The schema is checked into <c>schemas/v1/</c> and embedded into this assembly so consumers
/// (admin UIs, upload validators, package tooling) can validate JSON before sending it to a server
/// without inferring the contract from importer source. The schema is kept in sync with the
/// importer's strict shape by the CMP-0012 drift test. When the JSON contract changes, bump
/// <see cref="SchemaVersion"/> and the <c>v{n}</c> directory segment, and update the schema files
/// alongside the importer/exporter.
/// </remarks>
public static class SafeIrJsonSchemas
{
    private const string ResourcePrefix = "SafeIR.Serialization.Json.schemas.v1.";

    private const string ModuleResourceName = ResourcePrefix + "safe-ir-module.schema.json";

    /// <summary>
    /// Version of the JSON ingestion schema contract. Matches the <c>v1</c> directory segment and
    /// the <c>x-safe-ir-schema-version</c> field embedded in each schema document.
    /// </summary>
    public static string SchemaVersion => "1.0.0";

    /// <summary>
    /// JSON Schema document for the Safe IR module envelope
    /// (<see cref="SafeIrJsonImporter.Import(string)"/>).
    /// </summary>
    public static string ModuleEnvelope => ReadResource(ModuleResourceName);

    private static string ReadResource(string resourceName)
    {
        var assembly = typeof(SafeIrJsonSchemas).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded JSON schema resource '{resourceName}' was not found. " +
                "Ensure the schemas/v1 artifacts are embedded by SafeIR.Serialization.Json.csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

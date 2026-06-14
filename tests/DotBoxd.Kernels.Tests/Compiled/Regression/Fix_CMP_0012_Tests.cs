using System.Text.Json;
using System.Text.RegularExpressions;
using DotBoxd.Plugins;
using DotBoxd.Kernels.Serialization.Json;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for CMP-0012: the public JSON ingestion boundary (the module envelope
/// accepted by <see cref="DotBoxdJsonImporter.Import(string)"/> and the plugin package envelope
/// accepted by <see cref="DotBoxd.Plugins.PluginPackageJsonSerializer.Import(string)"/>) is the
/// documented text-ingestion path, yet the strict shape lived only in importer C# code. Consumers
/// (plugin authors, admin UIs, upload validators, package tooling) had to infer the accepted JSON
/// envelope from examples, tests, or importer source.
///
/// The fix ships versioned, machine-readable JSON Schema artifacts under <c>schemas/v1/</c>, embeds
/// them in <c>DotBoxd.Kernels.Serialization.Json</c>, and exposes them through
/// <see cref="DotBoxdJsonSchemas"/>. These tests pin the contract:
/// <list type="bullet">
///   <item>the artifacts exist on disk, are valid versioned JSON Schema, and are exposed via the
///   public API;</item>
///   <item>the schema's allowed-property lists stay synchronized with the importer's strict
///   <c>RequireAllowedProperties</c> lists (the drift guard from the finding's release-gate idea).</item>
/// </list>
/// </summary>
public sealed class Fix_CMP_0012_Tests
{
    private const string ModuleSchemaRelative = "schemas/v1/dotboxd-kernel-module.schema.json";
    private const string PackageSchemaRelative = "schemas/v1/dotboxd-plugin-package.schema.json";

    private const string ImporterRelative =
        "src/Kernels/DotBoxd.Kernels.Serialization.Json/DotBoxdJsonImporter.cs";

    private const string ExpressionReaderRelative =
        "src/Kernels/DotBoxd.Kernels.Serialization.Json/Internal/JsonExpressionReader.cs";

    private const string SerializerRelative =
        "src/Hosting/DotBoxd.Plugins/Json/PluginPackageJsonSerializer.cs";

    [Fact]
    public void Versioned_schema_artifacts_are_checked_in_under_a_version_segment()
    {
        var modulePath = RepositoryPath(ModuleSchemaRelative);
        var packagePath = RepositoryPath(PackageSchemaRelative);

        Assert.True(File.Exists(modulePath), $"Missing module schema artifact: {modulePath}");
        Assert.True(File.Exists(packagePath), $"Missing plugin package schema artifact: {packagePath}");

        // The artifacts must be versioned by a directory segment (v1, v2, ...) so the JSON contract
        // can evolve independently of the C# package APIs.
        Assert.Matches(@"schemas[\\/]v\d+[\\/]", modulePath.Replace('\\', '/'));
        Assert.Matches(@"schemas[\\/]v\d+[\\/]", packagePath.Replace('\\', '/'));
    }

    [Fact]
    public void Schema_artifacts_are_valid_versioned_json_schema_documents()
    {
        foreach (var relative in new[] { ModuleSchemaRelative, PackageSchemaRelative })
        {
            var json = File.ReadAllText(RepositoryPath(relative));
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            Assert.True(root.TryGetProperty("$schema", out var dialect), $"{relative} is missing $schema.");
            Assert.Contains("json-schema.org", dialect.GetString());
            Assert.True(root.TryGetProperty("$id", out _), $"{relative} is missing $id.");
            Assert.True(
                root.TryGetProperty("x-dotboxd-schema-version", out var version),
                $"{relative} is missing the schema version field.");
            Assert.False(string.IsNullOrWhiteSpace(version.GetString()));
        }
    }

    [Fact]
    public void Schema_artifacts_are_exposed_through_the_public_api_and_match_the_checked_in_files()
    {
        // The embedded copy exposed to consumers must be byte-for-byte the checked-in artifact, so
        // documentation links and the in-package contract cannot drift apart.
        Assert.Equal(
            Normalize(File.ReadAllText(RepositoryPath(ModuleSchemaRelative))),
            Normalize(DotBoxdJsonSchemas.ModuleEnvelope));
        Assert.Equal(
            Normalize(File.ReadAllText(RepositoryPath(PackageSchemaRelative))),
            Normalize(PluginPackageJsonSchemas.PackageEnvelope));

        // The exposed schema version must agree with the embedded documents and the v1 segment.
        Assert.Equal(SchemaVersionOf(DotBoxdJsonSchemas.ModuleEnvelope), DotBoxdJsonSchemas.SchemaVersion);
        Assert.Equal(SchemaVersionOf(PluginPackageJsonSchemas.PackageEnvelope), DotBoxdJsonSchemas.SchemaVersion);
    }

    [Theory]
    // Module envelope objects: schema $def name -> importer allowed-property list name.
    [InlineData(ModuleSchemaRelative, ImporterRelative, "module", new[] { "id", "version", "targetSandboxVersion", "capabilityRequests", "functions", "metadata" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "capability request", new[] { "id", "reason" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "function", new[] { "id", "visibility", "parameters", "returnType", "body" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "parameter", new[] { "name", "type" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "set statement", new[] { "op", "name", "value" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "if statement", new[] { "op", "condition", "then", "else" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "while statement", new[] { "op", "condition", "body" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "forRange statement", new[] { "op", "local", "start", "end", "body" })]
    [InlineData(ModuleSchemaRelative, ExpressionReaderRelative, "type", new[] { "name", "arguments" })]
    [InlineData(ModuleSchemaRelative, ExpressionReaderRelative, "call expression", new[] { "call", "args", "genericType" })]
    [InlineData(ModuleSchemaRelative, ExpressionReaderRelative, "unary expression", new[] { "unary", "operand" })]
    [InlineData(ModuleSchemaRelative, ExpressionReaderRelative, "binary expression", new[] { "op", "left", "right" })]
    // Plugin package envelope objects.
    [InlineData(PackageSchemaRelative, SerializerRelative, "plugin package", new[] { "manifest", "module", "entrypoints" })]
    [InlineData(PackageSchemaRelative, SerializerRelative, "plugin manifest", new[] { "pluginId", "contract", "mode", "effects", "liveSettings", "subscriptions", "requiredCapabilities", "rpcEntrypoint" })]
    [InlineData(PackageSchemaRelative, SerializerRelative, "live setting", new[] { "name", "type", "defaultValue", "min", "max" })]
    [InlineData(PackageSchemaRelative, SerializerRelative, "hook subscription", new[] { "event", "kernel" })]
    [InlineData(PackageSchemaRelative, SerializerRelative, "kernel entrypoints", new[] { "shouldHandle", "handle" })]
    public void Schema_allowed_properties_stay_synchronized_with_importer_strict_shape(
        string schemaRelative,
        string sourceRelative,
        string requirementName,
        string[] expectedAllowed)
    {
        // 1. The importer source is the canonical strict shape: the RequireAllowedProperties call for
        //    this object must list exactly the expected property names. This catches the importer
        //    drifting from the schema even if the test's expected list is what changed.
        var importerAllowed = ImporterAllowedProperties(sourceRelative, requirementName);
        Assert.True(
            SameSet(importerAllowed, expectedAllowed),
            $"Importer allowed-property list for '{requirementName}' ({sourceRelative}) drifted from the " +
            $"schema contract. Importer: [{string.Join(", ", importerAllowed)}]. Schema/test: [{string.Join(", ", expectedAllowed)}].");

        // 2. The schema must declare exactly the same property set for this object, so a consumer
        //    validating against the artifact accepts and rejects the same envelope as the importer.
        var schemaProperties = SchemaPropertyNamesFor(schemaRelative, expectedAllowed);
        Assert.True(
            SameSet(schemaProperties, expectedAllowed),
            $"JSON schema '{schemaRelative}' allowed properties for '{requirementName}' drifted from the " +
            $"importer. Schema: [{string.Join(", ", schemaProperties)}]. Importer: [{string.Join(", ", expectedAllowed)}].");
    }

    /// <summary>
    /// Extracts the property-name array passed to <c>RequireAllowedProperties(..., "name", [..])</c>
    /// for the given requirement name from the maintained importer source.
    /// </summary>
    private static IReadOnlyList<string> ImporterAllowedProperties(string sourceRelative, string requirementName)
    {
        var source = File.ReadAllText(RepositoryPath(sourceRelative));
        var pattern = "RequireAllowedProperties\\([^;]*?\"" +
            Regex.Escape(requirementName) +
            "\"\\s*,\\s*\\[(?<props>[^\\]]*)\\]";
        var match = Regex.Match(source, pattern, RegexOptions.Singleline);
        Assert.True(
            match.Success,
            $"Could not find a RequireAllowedProperties list for '{requirementName}' in {sourceRelative}.");

        return Regex.Matches(match.Groups["props"].Value, "\"(?<name>[^\"]+)\"")
            .Select(m => m.Groups["name"].Value)
            .ToList();
    }

    /// <summary>
    /// Finds, anywhere in the schema document, the object whose <c>properties</c> map exactly covers
    /// the expected property names and returns that property set. Anchoring on the expected set keeps
    /// the lookup independent of where the object is declared in the schema ($defs vs inline).
    /// </summary>
    private static IReadOnlyList<string> SchemaPropertyNamesFor(string schemaRelative, string[] expectedAllowed)
    {
        var json = File.ReadAllText(RepositoryPath(schemaRelative));
        using var document = JsonDocument.Parse(json);
        var expected = expectedAllowed.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        var best = FindMatchingPropertySet(document.RootElement, expected);
        Assert.True(
            best is not null,
            $"No object in {schemaRelative} declares a 'properties' map matching [{string.Join(", ", expectedAllowed)}].");
        return best!;
    }

    private static IReadOnlyList<string>? FindMatchingPropertySet(JsonElement element, string[] expected)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("properties", out var properties) &&
                    properties.ValueKind == JsonValueKind.Object)
                {
                    var names = properties.EnumerateObject().Select(p => p.Name).ToArray();
                    if (SameSet(names, expected))
                    {
                        return names;
                    }
                }

                foreach (var child in element.EnumerateObject())
                {
                    var found = FindMatchingPropertySet(child.Value, expected);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return null;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var found = FindMatchingPropertySet(item, expected);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private static bool SameSet(IEnumerable<string> left, IEnumerable<string> right)
        => new HashSet<string>(left, StringComparer.Ordinal)
            .SetEquals(new HashSet<string>(right, StringComparer.Ordinal));

    private static string SchemaVersionOf(string schemaJson)
    {
        using var document = JsonDocument.Parse(schemaJson);
        return document.RootElement.GetProperty("x-dotboxd-schema-version").GetString()!;
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    private static string RepositoryPath(string relativePath)
        => Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DotBoxd.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}

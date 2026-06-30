using System.Text.Json;
using System.Text.RegularExpressions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Schema;
using DotBoxD.Kernels.Tests.Compiled.Regression.SchemaDrift;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Tests.Compiled.Regression;

/// <summary>
/// Regression coverage for CMP-0012: the public JSON ingestion boundary (the module envelope
/// accepted by <see cref="JsonImporter.Import(string)"/> and the plugin package envelope
/// accepted by <see cref="PluginPackageJsonSerializer.Import"/>) is the
/// documented text-ingestion path, yet the strict shape lived only in importer C# code. Consumers
/// (plugin authors, admin UIs, upload validators, package tooling) had to infer the accepted JSON
/// envelope from examples, tests, or importer source.
///
/// The fix ships versioned, machine-readable JSON Schema artifacts under <c>schemas/v1/</c>, embeds
/// them in <c>DotBoxD.Kernels.Serialization.Json</c>, and exposes them through
/// <see cref="JsonSchemas"/>. These tests pin the contract:
/// <list type="bullet">
///   <item>the artifacts exist on disk, are valid versioned JSON Schema, and are exposed via the
///   public API;</item>
///   <item>the schema's strict object shapes stay synchronized with the importer's
///   <c>RequireAllowedProperties</c> lists and required/discriminator constraints.</item>
/// </list>
/// </summary>
public sealed class Fix_CMP_0012_Tests
{
    private const string ModuleSchemaRelative = "schemas/v1/dotboxd-kernel-module.schema.json";
    private const string PackageSchemaRelative = "schemas/v1/dotboxd-plugin-package.schema.json";

    private const string ImporterRelative =
        "src/Kernels/DotBoxD.Kernels.Serialization.Json/JsonImporter.cs";

    private const string ExpressionReaderRelative =
        "src/Kernels/DotBoxD.Kernels.Serialization.Json/Internal/JsonExpressionReader.cs";

    private const string SerializerRelative =
        "src/Hosting/DotBoxD.Plugins/Json/PluginPackageJsonSerializer.cs";

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
            Normalize(JsonSchemas.ModuleEnvelope));
        Assert.Equal(
            Normalize(File.ReadAllText(RepositoryPath(PackageSchemaRelative))),
            Normalize(PluginPackageJsonSchemas.PackageEnvelope));

        // The exposed schema version must agree with the embedded documents and the v1 segment.
        Assert.Equal(SchemaVersionOf(JsonSchemas.ModuleEnvelope), JsonSchemas.SchemaVersion);
        Assert.Equal(SchemaVersionOf(PluginPackageJsonSchemas.PackageEnvelope), JsonSchemas.SchemaVersion);
    }

    [Theory]
    // Module envelope objects: schema $def name -> importer allowed-property list name.
    [InlineData(ModuleSchemaRelative, ImporterRelative, "module", new[] { "id", "version", "targetSandboxVersion", "capabilityRequests", "functions", "metadata" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "capability request", new[] { "id", "reason" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "function", new[] { "id", "visibility", "parameters", "returnType", "body" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "parameter", new[] { "name", "type" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "set statement", new[] { "op", "name", "value" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "return statement", new[] { "op", "value" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "expression statement", new[] { "op", "value" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "if statement", new[] { "op", "condition", "then", "else" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "while statement", new[] { "op", "condition", "body" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "forRange statement", new[] { "op", "local", "start", "end", "body" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "continue statement", new[] { "op" })]
    [InlineData(ModuleSchemaRelative, ImporterRelative, "break statement", new[] { "op" })]
    [InlineData(ModuleSchemaRelative, ExpressionReaderRelative, "type", new[] { "name", "arguments" })]
    [InlineData(ModuleSchemaRelative, ExpressionReaderRelative, "variable expression", new[] { "var" })]
    [InlineData(ModuleSchemaRelative, ExpressionReaderRelative, "call expression", new[] { "call", "args", "genericType" })]
    [InlineData(ModuleSchemaRelative, ExpressionReaderRelative, "unary expression", new[] { "unary", "operand" })]
    [InlineData(ModuleSchemaRelative, ExpressionReaderRelative, "binary expression", new[] { "op", "left", "right" })]
    // Plugin package envelope objects.
    [InlineData(PackageSchemaRelative, SerializerRelative, "plugin package", new[] { "manifest", "module", "entrypoints" })]
    [InlineData(PackageSchemaRelative, SerializerRelative, "plugin manifest", new[] { "pluginId", "contract", "mode", "effects", "liveSettings", "subscriptions", "requiredCapabilities", "rpcEntrypoint" })]
    [InlineData(PackageSchemaRelative, SerializerRelative, "live setting", new[] { "name", "type", "defaultValue", "min", "max" })]
    [InlineData(PackageSchemaRelative, SerializerRelative, "hook subscription", new[] { "event", "kernel", "indexedPredicates", "indexCoversPredicate", "localTerminal", "projectedType", "priority", "resultType", "resultLocalTerminal" })]
    [InlineData(PackageSchemaRelative, SerializerRelative, "indexed predicate", new[] { "path", "operator", "value", "valueType" })]
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

        // 2. The schema must declare the same strict object contract, so a consumer validating
        //    against the artifact accepts and rejects the same envelope as the importer.
        var schemaFailures = JsonSchemaDriftGuard.SemanticDriftMessages(
            File.ReadAllText(RepositoryPath(schemaRelative)),
            JsonSchemaContractCatalog.ForImporterShape(requirementName, expectedAllowed));
        Assert.True(
            schemaFailures.Count == 0,
            $"JSON schema '{schemaRelative}' strict shape for '{requirementName}' drifted from the importer. " +
            string.Join(" ", schemaFailures));
    }

    [Fact]
    public void Plugin_package_schema_value_domains_match_importer()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryPath(PackageSchemaRelative)));
        var defs = document.RootElement.GetProperty("$defs");
        var manifestProperties = defs.GetProperty("manifest").GetProperty("properties");

        AssertPattern(
            manifestProperties.GetProperty("mode"),
            "^(?:[Aa][Uu][Tt][Oo]|[Ii][Nn][Tt][Ee][Rr][Pp][Rr][Ee][Tt][Ee][Dd]|[Cc][Oo][Mm][Pp][Ii][Ll][Ee][Dd])$");
        AssertEnum(
            manifestProperties.GetProperty("effects").GetProperty("items"),
            Enum.GetNames<SandboxEffect>().Where(name => name != nameof(SandboxEffect.None)));
        AssertEnum(
            defs.GetProperty("liveSetting").GetProperty("properties").GetProperty("type"),
            ["bool", "int", "long", "double", "string"]);
    }

    [Fact]
    public void Module_schema_i32_literal_range_matches_importer()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryPath(ModuleSchemaRelative)));
        var i32 = document.RootElement
            .GetProperty("$defs")
            .GetProperty("literal")
            .GetProperty("properties")
            .GetProperty("i32");

        Assert.True(i32.TryGetProperty("minimum", out var minimum), "i32 schema is missing minimum.");
        Assert.Equal(int.MinValue, minimum.GetInt32());

        Assert.True(i32.TryGetProperty("maximum", out var maximum), "i32 schema is missing maximum.");
        Assert.Equal(int.MaxValue, maximum.GetInt32());
    }

    [Fact]
    public void Module_schema_i64_and_f64_literal_ranges_match_importer()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryPath(ModuleSchemaRelative)));
        var literalProperties = document.RootElement
            .GetProperty("$defs")
            .GetProperty("literal")
            .GetProperty("properties");
        var i64 = literalProperties.GetProperty("i64");
        var f64 = literalProperties.GetProperty("f64");

        Assert.True(i64.TryGetProperty("minimum", out var i64Minimum), "i64 schema is missing minimum.");
        Assert.Equal(long.MinValue, i64Minimum.GetInt64());

        Assert.True(i64.TryGetProperty("maximum", out var i64Maximum), "i64 schema is missing maximum.");
        Assert.Equal(long.MaxValue, i64Maximum.GetInt64());

        Assert.True(f64.TryGetProperty("minimum", out var f64Minimum), "f64 schema is missing minimum.");
        Assert.Equal(-double.MaxValue, f64Minimum.GetDouble());

        Assert.True(f64.TryGetProperty("maximum", out var f64Maximum), "f64 schema is missing maximum.");
        Assert.Equal(double.MaxValue, f64Maximum.GetDouble());
    }

    [Fact]
    public void Module_schema_guid_literal_domain_matches_importer()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryPath(ModuleSchemaRelative)));
        var guid = document.RootElement
            .GetProperty("$defs")
            .GetProperty("literal")
            .GetProperty("properties")
            .GetProperty("guid");

        Assert.Equal("string", guid.GetProperty("type").GetString());
        AssertPattern(guid, "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");
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

    private static bool SameSet(IEnumerable<string> left, IEnumerable<string> right)
        => new HashSet<string>(left, StringComparer.Ordinal)
            .SetEquals(new HashSet<string>(right, StringComparer.Ordinal));

    private static void AssertEnum(JsonElement schema, IEnumerable<string> expected)
    {
        Assert.True(schema.TryGetProperty("enum", out var actual), "schema is missing enum.");
        var actualValues = actual.EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.True(
            SameSet(actualValues, expected),
            $"Schema enum drifted. Schema: [{string.Join(", ", actualValues)}]. Expected: [{string.Join(", ", expected)}].");
    }

    private static void AssertPattern(JsonElement schema, string expected)
    {
        Assert.True(schema.TryGetProperty("pattern", out var actual), "schema is missing pattern.");
        Assert.Equal(expected, actual.GetString());
    }

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
            if (File.Exists(Path.Combine(current.FullName, "DotBoxD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}

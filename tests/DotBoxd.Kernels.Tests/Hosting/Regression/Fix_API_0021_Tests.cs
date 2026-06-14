using System.Text.RegularExpressions;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for API-0021: <see cref="DotBoxdJsonExporter"/> is a public module
/// serialization API in the <c>DotBoxd.Kernels.Serialization.Json</c> package, but the public package
/// guidance and the package-backed release smoke previously only proved JSON import and plugin
/// package upload. A consumer could discover <c>DotBoxdJsonImporter</c> and
/// <c>PluginPackageJsonSerializer</c> from the README/smoke while the module export surface was
/// only visible in source and the API spec, so exporter namespace/package/dependency drift could
/// slip past every release gate.
///
/// These tests pin the round-trip contract the guidance promises and pin the README + package
/// consumer smoke so they cannot silently regress back to an import-only surface:
/// <list type="bullet">
///   <item>the exporter actually round-trips a module through the public Export/Import pair;</item>
///   <item>the README package list and common namespaces name <c>DotBoxdJsonExporter</c>;</item>
///   <item>the package consumer smoke references and calls <c>DotBoxdJsonExporter.Export(...)</c>.</item>
/// </list>
/// </summary>
public sealed class Fix_API_0021_Tests
{
    private static readonly SourceSpan Span = new(1, 1);

    [Fact]
    public void Exporter_round_trips_a_module_through_the_public_export_import_pair()
    {
        var module = new SandboxModule(
            "api-0021-roundtrip",
            SemVersion.One,
            SemVersion.One,
            [],
            [
                new SandboxFunction(
                    "main",
                    true,
                    [],
                    SandboxType.I32,
                    [new ReturnStatement(new LiteralExpression(SandboxValue.FromInt32(7), Span), Span)])
            ],
            new Dictionary<string, string>());

        var json = DotBoxdJsonExporter.Export(module, indented: true);
        var roundTrip = DotBoxdJsonImporter.Import(json);

        Assert.Equal(module.Id, roundTrip.Id);
        var function = Assert.Single(roundTrip.Functions);
        Assert.Equal("main", function.Id);
        Assert.True(function.IsEntrypoint);
        var ret = Assert.IsType<ReturnStatement>(Assert.Single(function.Body));
        var literal = Assert.IsType<LiteralExpression>(ret.Value);
        Assert.Equal(7, Assert.IsType<I32Value>(literal.Value).Value);
    }

    [Fact]
    public void Readme_names_the_exporter_in_package_guidance_and_common_namespaces()
    {
        var readme = ReadRepositoryText("README.md");

        // The DotBoxd.Kernels.Serialization.Json package entry must describe the export surface, not just
        // import and plugin upload, so users can discover the module export side of the round trip.
        Assert.Matches(
            new Regex(@"`DotBoxd.Kernels\.Serialization\.Json`:[^\r\n]*export", RegexOptions.IgnoreCase),
            readme);

        // The common-namespaces guidance must name DotBoxdJsonExporter alongside the importer so the
        // documented round-trip entrypoint is discoverable from install guidance.
        Assert.Contains("DotBoxdJsonExporter", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Package_consumer_smoke_references_and_calls_the_exporter()
    {
        var script = ReadRepositoryText(Path.Combine("eng", "scripts", "check-package-consumer-smoke.ps1"));

        // The smoke must do more than reference the importer/upload types: it has to exercise the
        // packaged exporter so a dropped namespace, wrong package placement, or missing transitive
        // dependency fails to compile instead of passing on import-only coverage.
        Assert.Contains("DotBoxdJsonExporter.Export(", script, StringComparison.Ordinal);

        // Round-tripping the exported JSON back through the importer and preparing it proves the
        // documented export -> import -> prepare path end to end through the public packages.
        Assert.Contains("DotBoxdJsonImporter.Import(", script, StringComparison.Ordinal);
        Assert.Contains("PrepareAsync(reimported", script, StringComparison.Ordinal);
    }

    private static string ReadRepositoryText(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath);
        Assert.True(File.Exists(path), $"Missing repository file: {path}");
        return File.ReadAllText(path);
    }

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

using System.Text.Json;
using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression tests for ALG-0006: plugin package import must read the embedded module
/// directly from the already-parsed JSON element instead of re-parsing its raw text, while
/// preserving the exact source spans produced by a standalone module import.
/// </summary>
public sealed class Fix_ALG_0006_Tests
{
    [Fact]
    public void Package_import_preserves_module_source_spans_of_standalone_import()
    {
        var json = PackageJson();
        var moduleText = ModuleRawText(json);

        var standalone = DotBoxdJsonImporter.Import(moduleText);
        var package = PluginPackageJsonSerializer.Import(json);

        var standaloneStatement = standalone.Functions
            .Single(f => f.Id == "Handle")
            .Body.Single();
        var packageStatement = package.Module.Functions
            .Single(f => f.Id == "Handle")
            .Body.Single();

        // Spans must remain relative to the module's own text, identical to a standalone import.
        Assert.Equal(standaloneStatement.Span.Line, packageStatement.Span.Line);
        Assert.Equal(standaloneStatement.Span.Column, packageStatement.Span.Column);
    }

    [Fact]
    public void Package_import_round_trips_module_structure()
    {
        var package = PluginPackageJsonSerializer.Import(PackageJson());
        var module = package.Module;

        Assert.Equal("alg-0006-module", module.Id);
        var function = module.Functions.Single(f => f.Id == "Handle");
        Assert.Equal("Handle", function.Id);
        var ret = Assert.IsType<ReturnStatement>(function.Body.Single());
        var literal = Assert.IsType<LiteralExpression>(ret.Value);
        Assert.NotEqual(0, literal.Span.Line);
    }

    private static string ModuleRawText(string packageJson)
    {
        using var document = JsonDocument.Parse(packageJson);
        return document.RootElement.GetProperty("module").GetRawText();
    }

    private static string PackageJson()
        => """
        {
          "manifest": {
            "pluginId": "alg-0006-module",
            "contract": "IEventKernel<DamageEvent>",
            "mode": "Interpreted",
            "effects": ["Cpu"],
            "liveSettings": [],
            "subscriptions": [
              { "event": "DamageEvent", "kernel": "Alg0006Kernel" }
            ]
          },
          "module": {
            "id": "alg-0006-module",
            "version": "1.0.0",
            "metadata": { "pluginId": "alg-0006-module", "kernel": "Alg0006Kernel" },
            "functions": [
              {
                "id": "ShouldHandle",
                "visibility": "entrypoint",
                "parameters": [],
                "returnType": "Bool",
                "body": [
                  { "op": "return", "value": { "bool": true } }
                ]
              },
              {
                "id": "Handle",
                "visibility": "entrypoint",
                "parameters": [],
                "returnType": "I32",
                "body": [
                  { "op": "return", "value": { "i32": 7 } }
                ]
              }
            ]
          }
        }
        """;
}

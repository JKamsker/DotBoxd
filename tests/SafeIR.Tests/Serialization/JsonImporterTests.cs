using SafeIR;
using SafeIR.Hosting;

namespace SafeIR.Tests;

public sealed class JsonImporterTests
{
    [Fact]
    public void Canonical_hash_is_stable_across_json_property_order()
    {
        var first = SafeIrJsonImporter.Import(SandboxTestHost.PureScoreJson());
        var second = SafeIrJsonImporter.Import("""
        {
          "functions": [
            {
              "body": [
                { "value": { "right": { "i32": 10 }, "left": { "var": "level" }, "op": "mul" }, "name": "base", "op": "set" },
                { "name": "bonus", "op": "set", "value": { "left": { "var": "rarity" }, "op": "mul", "right": { "i32": 25 } } },
                { "op": "return", "value": { "op": "add", "left": { "var": "base" }, "right": { "var": "bonus" } } }
              ],
              "returnType": "I32",
              "parameters": [
                { "type": "I32", "name": "level" },
                { "type": "I32", "name": "rarity" }
              ],
              "visibility": "entrypoint",
              "id": "main"
            }
          ],
          "capabilityRequests": [],
          "targetSandboxVersion": "1.0.0",
          "version": "1.0.0",
          "id": "loot-score"
        }
        """);

        Assert.Equal(CanonicalModuleHasher.Hash(first), CanonicalModuleHasher.Hash(second));
    }

    [Fact]
    public void Capability_requests_reject_untrusted_grant_parameters()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import("""
        {
          "id": "bad",
          "version": "1.0.0",
          "capabilityRequests": [
            { "id": "file.read", "root": "C:\\" }
          ],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-SCHEMA");
    }

    [Fact]
    public void Module_root_rejects_unsupported_properties()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import(MinimalModule(
            """
            "assemblyName": "System.IO.File",
            """)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-SCHEMA");
    }

    [Fact]
    public void Expression_rejects_mixed_shapes()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import(MinimalModule(
            "",
            """{ "i32": 1, "call": "math.abs", "args": [{ "i32": 1 }] }""")));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-SCHEMA");
    }

    [Theory]
    [InlineData("""{ "i32": "1" }""")]
    [InlineData("""{ "i64": true }""")]
    [InlineData("""{ "f64": "1.5" }""")]
    [InlineData("""{ "bool": 1 }""")]
    public void Literal_values_reject_wrong_json_kinds(string expression)
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import(MinimalModule("", expression)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-TYPE");
    }

    [Fact]
    public void If_else_rejects_non_array_shape()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import("""
        {
          "id": "bad-else",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "if",
                  "condition": { "bool": true },
                  "then": [{ "op": "return", "value": { "i32": 1 } }],
                  "else": { "op": "return", "value": { "i32": 2 } }
                }
              ]
            }
          ]
        }
        """));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-TYPE");
    }

    [Fact]
    public void Function_rejects_unknown_visibility()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import("""
        {
          "id": "bad-visibility",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "public",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-VISIBILITY");
    }

    [Fact]
    public async Task Duplicate_parameter_names_return_validation_diagnostic()
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync("""
        {
          "id": "bad-params",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                { "name": "value", "type": "I32" },
                { "name": "value", "type": "I32" }
              ],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "var": "value" } }]
            }
          ]
        }
        """);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-STRUCT-DUP-PARAM");
    }

    [Fact]
    public async Task Metadata_rejects_forbidden_clr_references()
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(MinimalModule(
            """
            "metadata": { "debug": "System.IO.File" },
            """));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-IR-CLR-REF");
    }

    [Theory]
    [InlineData("1.1.0")]
    [InlineData("2.0.0")]
    public async Task Unsupported_target_sandbox_version_is_rejected(string targetSandboxVersion)
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(MinimalModule(
            $"""
            "targetSandboxVersion": "{targetSandboxVersion}",
            """));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-IR-VERSION");
    }

    [Theory]
    [InlineData("""{ "op": "pow", "left": { "i32": 2 }, "right": { "i32": 8 } }""")]
    [InlineData("""{ "unary": "bitwiseNot", "operand": { "i32": 1 } }""")]
    public void Unknown_expression_operators_are_rejected_during_import(string expression)
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import(MinimalModule("", expression)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-OP");
    }

    [Fact]
    public async Task Missing_target_sandbox_version_defaults_to_current_language_version()
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(MinimalModule(""));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build());

        Assert.Equal(SandboxLanguage.CurrentVersion, plan.Module.TargetSandboxVersion);
    }

    [Fact]
    public async Task Forbidden_clr_call_is_rejected_before_execution()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "bad",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "String",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "System.IO.File.ReadAllText",
                    "args": [{ "string": "secret.txt" }]
                  }
                }
              ]
            }
          ]
        }
        """);

        var policy = SandboxPolicyBuilder.Create().Build();
        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await host.PrepareAsync(module, policy));
        Assert.Contains(ex.Diagnostics, d => d.Code == "E-IR-CLR-REF");
    }

    private static string MinimalModule(string extraModuleProperty, string returnValue = """{ "i32": 1 }""")
        => $$"""
        {
          "id": "schema-check",
          "version": "1.0.0",
          {{extraModuleProperty}}
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": {{returnValue}} }]
            }
          ]
        }
        """;
}

using System.Globalization;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Core;

public sealed class CanonicalModuleHasherTests
{
    [Fact]
    public void Identical_modules_share_canonical_hash()
    {
        var first = JsonImporter.Import(SumModule());
        var second = JsonImporter.Import(SumModule());

        Assert.Equal(CanonicalModuleHasher.Hash(first), CanonicalModuleHasher.Hash(second));
    }

    [Fact]
    public void Canonical_serialization_records_canonicalizer_version()
    {
        var module = JsonImporter.Import(SumModule());
        var serialized = CanonicalModuleHasher.Serialize(module);

        Assert.Contains(CanonicalModuleHasher.CanonicalizerVersion, serialized);
    }

    [Fact]
    public void Statement_order_is_semantic()
    {
        var declareThenOther = JsonImporter.Import(ModuleWithBody(
            """
            { "op": "set", "name": "first", "value": { "i32": 1 } },
            { "op": "set", "name": "second", "value": { "i32": 2 } },
            { "op": "return", "value": { "var": "first" } }
            """));
        var swapped = JsonImporter.Import(ModuleWithBody(
            """
            { "op": "set", "name": "second", "value": { "i32": 2 } },
            { "op": "set", "name": "first", "value": { "i32": 1 } },
            { "op": "return", "value": { "var": "first" } }
            """));

        Assert.NotEqual(CanonicalModuleHasher.Hash(declareThenOther), CanonicalModuleHasher.Hash(swapped));
    }

    [Fact]
    public void Operand_order_is_semantic()
    {
        var left = JsonImporter.Import(ModuleWithReturn(
            """{ "op": "sub", "left": { "i32": 1 }, "right": { "i32": 2 } }""",
            "I32"));
        var right = JsonImporter.Import(ModuleWithReturn(
            """{ "op": "sub", "left": { "i32": 2 }, "right": { "i32": 1 } }""",
            "I32"));

        Assert.NotEqual(CanonicalModuleHasher.Hash(left), CanonicalModuleHasher.Hash(right));
    }

    [Fact]
    public void Literal_authored_type_affects_hash()
    {
        var asInt = JsonImporter.Import(ModuleWithReturn("""{ "i32": 1 }""", "I32"));
        var asDouble = JsonImporter.Import(ModuleWithReturn("""{ "f64": 1.0 }""", "F64"));

        Assert.NotEqual(CanonicalModuleHasher.Hash(asInt), CanonicalModuleHasher.Hash(asDouble));
    }

    [Fact]
    public void Parameter_name_is_part_of_hash()
    {
        var first = JsonImporter.Import(ModuleWithParameter("p0"));
        var second = JsonImporter.Import(ModuleWithParameter("p1"));

        Assert.NotEqual(CanonicalModuleHasher.Hash(first), CanonicalModuleHasher.Hash(second));
    }

    [Fact]
    public void Capability_requests_with_matching_ids_are_ordered_by_reason()
    {
        var first = ModuleWithCapabilityRequests([
            new CapabilityRequest("host.message.write", "beta"),
            new CapabilityRequest("host.message.write", "alpha")
        ]);
        var second = ModuleWithCapabilityRequests([
            new CapabilityRequest("host.message.write", "alpha"),
            new CapabilityRequest("host.message.write", "beta")
        ]);

        Assert.Equal(CanonicalModuleHasher.Serialize(first), CanonicalModuleHasher.Serialize(second));
        Assert.Equal(CanonicalModuleHasher.Hash(first), CanonicalModuleHasher.Hash(second));
    }

    [Fact]
    public void Floating_point_literals_hash_independently_of_current_culture()
    {
        var module = JsonImporter.Import(ModuleWithReturn("""{ "f64": 1.5 }""", "F64"));
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var germanHash = CanonicalModuleHasher.Hash(module);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var englishHash = CanonicalModuleHasher.Hash(module);
            var serialized = CanonicalModuleHasher.Serialize(module);

            Assert.Equal(germanHash, englishHash);
            Assert.Contains("1.5", serialized);
            Assert.DoesNotContain("1,5", serialized);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Canonical_serialization_escapes_internal_control_separators()
    {
        var module = JsonImporter.Import(ModuleWithReturn(
            """{ "string": "a\u0000b\u001fc\r\n\t\\d" }""",
            "String"));

        var serialized = CanonicalModuleHasher.Serialize(module);

        Assert.Contains(@"\u0000", serialized);
        Assert.Contains("u001f", serialized);
        Assert.Contains(@"\t", serialized);
        Assert.DoesNotContain(serialized, c => c == (char)0x1f);
        Assert.DoesNotContain(serialized, c => c == (char)0x00);
        Assert.DoesNotContain(serialized, c => c == '\t');
        Assert.DoesNotContain("\r", serialized);
    }

    [Fact]
    public void Canonical_serialization_rejects_unknown_statement_shape()
    {
        var module = new SandboxModule(
            "unknown-statement",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    true,
                    [],
                    SandboxType.Unit,
                    [new UnknownStatement(new SourceSpan(0, 0))])
            ],
            new Dictionary<string, string>());

        Assert.Throws<NotSupportedException>(() => CanonicalModuleHasher.Serialize(module));
    }

    [Fact]
    public void Canonical_hash_distinguishes_delimiter_heavy_expression_shapes()
    {
        var first = ModuleWithVariableAdd("a),var(b", "c");
        var second = ModuleWithVariableAdd("a", "b),var(c");

        Assert.NotEqual(CanonicalModuleHasher.Serialize(first), CanonicalModuleHasher.Serialize(second));
        Assert.NotEqual(CanonicalModuleHasher.Hash(first), CanonicalModuleHasher.Hash(second));
    }

    private static string ModuleWithReturn(string expression, string returnType)
        => $$"""
        {
          "id": "canonical-literals",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "{{returnType}}",
              "body": [{ "op": "return", "value": {{expression}} }]
            }
          ]
        }
        """;

    private static SandboxModule ModuleWithVariableAdd(string left, string right)
        => new(
            "canonical-expressions",
            SemVersion.One,
            SemVersion.One,
            [],
            [
                new SandboxFunction(
                    "main",
                    true,
                    [],
                    SandboxType.I32,
                    [
                        new ReturnStatement(
                            new BinaryExpression(
                                new VariableExpression(left, new SourceSpan(0, 0)),
                                "+",
                                new VariableExpression(right, new SourceSpan(0, 0)),
                                new SourceSpan(0, 0)),
                            new SourceSpan(0, 0))
                    ])
            ],
            new Dictionary<string, string>());

    private static SandboxModule ModuleWithCapabilityRequests(IReadOnlyList<CapabilityRequest> requests)
        => new(
            "canonical-capabilities",
            SemVersion.One,
            SemVersion.One,
            requests,
            [
                new SandboxFunction(
                    "main",
                    true,
                    [],
                    SandboxType.Unit,
                    [
                        new ReturnStatement(
                            new LiteralExpression(SandboxValue.Unit, new SourceSpan(0, 0)),
                            new SourceSpan(0, 0))
                    ])
            ],
            new Dictionary<string, string>());

    private static string ModuleWithBody(string body)
        => $$"""
        {
          "id": "canonical-statements",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {{body}}
              ]
            }
          ]
        }
        """;

    private static string ModuleWithParameter(string name)
        => $$"""
        {
          "id": "canonical-parameters",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "{{name}}", "type": "I32" }],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "var": "{{name}}" } }]
            }
          ]
        }
        """;

    private static string SumModule()
        => """
        {
          "id": "canonical-sum",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "n", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "sum", "value": { "i32": 0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 1 },
                  "end": { "var": "n" },
                  "body": [
                    {
                      "op": "set",
                      "name": "sum",
                      "value": { "op": "add", "left": { "var": "sum" }, "right": { "var": "i" } }
                    }
                  ]
                },
                { "op": "return", "value": { "var": "sum" } }
              ]
            }
          ]
        }
        """;

    private sealed record UnknownStatement(SourceSpan Span) : Statement(Span);
}

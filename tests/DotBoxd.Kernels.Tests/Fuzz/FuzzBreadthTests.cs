using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotBoxd.Kernels.Verifier;

namespace DotBoxd.Kernels.Tests;

public sealed class FuzzBreadthTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] Names = ["a", "b", "c", "value"];

    [Fact]
    public void Json_importer_fuzz_rejects_malformed_expression_shapes_with_diagnostics()
    {
        var random = new Random(0x51AFE001);

        for (var i = 0; i < 60; i++)
        {
            var json = ModuleJson(i, InvalidExpression(random).ToJsonString(JsonOptions));

            var ex = Assert.Throws<SandboxValidationException>(() => DotBoxdJsonImporter.Import(json));

            Assert.NotEmpty(ex.Diagnostics);
            Assert.All(ex.Diagnostics, d => Assert.StartsWith("E-JSON-", d.Code, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Canonical_hash_fuzz_is_stable_across_json_property_order()
    {
        var random = new Random(0x51AFE002);

        for (var i = 0; i < 40; i++)
        {
            var expression = ValidExpression(random, depth: 4).ToJsonString(JsonOptions);
            var first = DotBoxdJsonImporter.Import(ModuleJson(i, expression, shuffled: false));
            var second = DotBoxdJsonImporter.Import(ModuleJson(i, expression, shuffled: true));

            Assert.Equal(CanonicalModuleHasher.Hash(first), CanonicalModuleHasher.Hash(second));
        }
    }

    [Fact]
    public void Policy_hash_fuzz_distinguishes_parameter_and_limit_values()
    {
        var random = new Random(0x51AFE003);
        var hashes = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 40; i++)
        {
            var policy = SandboxPolicyBuilder.Create()
                .WithPolicyId("fuzz-policy")
                .Grant("fuzz.cap", new Dictionary<string, string>
                {
                    ["tenant"] = Token(random),
                    ["limit"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }, SandboxEffect.Cpu)
                .WithFuel(1_000 + i)
                .Build();

            Assert.True(hashes.Add(policy.Hash), $"duplicate policy hash at case {i}");
        }
    }

    [Fact]
    public void Policy_hash_is_stable_across_grant_parameter_order()
    {
        var first = SandboxPolicyBuilder.Create()
            .Grant("fuzz.cap", new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" }, SandboxEffect.Cpu)
            .Build();
        var second = SandboxPolicyBuilder.Create()
            .Grant("fuzz.cap", new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" }, SandboxEffect.Cpu)
            .Build();

        Assert.Equal(first.Hash, second.Hash);
    }

    [Fact]
    public async Task Verifier_fuzz_reports_diagnostics_for_malformed_bytes()
    {
        var random = new Random(0x51AFE004);
        var verifier = new GeneratedAssemblyVerifier();
        var policy = VerificationPolicy.BoxedValueDefaults();

        for (var i = 0; i < 30; i++)
        {
            var bytes = new byte[random.Next(1, 256)];
            random.NextBytes(bytes);
            var result = await verifier.VerifyAsync(bytes, Manifest(bytes), policy, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.NotEmpty(result.Diagnostics);
        }
    }

    private static JsonObject InvalidExpression(Random random)
        => random.Next(7) switch
        {
            0 => new JsonObject { ["var"] = 42 },
            1 => new JsonObject { ["i32"] = 1, ["bool"] = true },
            2 => new JsonObject { ["call"] = "math.abs", ["args"] = new JsonObject { ["i32"] = 1 } },
            3 => new JsonObject { ["op"] = "add", ["left"] = new JsonObject { ["i32"] = 1 } },
            4 => new JsonObject { ["path"] = "../secret.txt" },
            5 => new JsonObject { ["uri"] = "not-a-uri" },
            _ => new JsonObject { ["unary"] = "not", ["operand"] = new JsonArray() }
        };

    private static JsonObject ValidExpression(Random random, int depth)
    {
        if (depth == 0 || random.Next(4) == 0)
        {
            return random.Next(2) == 0
                ? new JsonObject { ["i32"] = random.Next(-10, 11) }
                : new JsonObject { ["var"] = Names[random.Next(Names.Length)] };
        }

        return new JsonObject
        {
            ["op"] = random.Next(3) switch { 0 => "add", 1 => "sub", _ => "mul" },
            ["left"] = ValidExpression(random, depth - 1),
            ["right"] = ValidExpression(random, depth - 1)
        };
    }

    private static string ModuleJson(int index, string expression, bool shuffled = false)
        => shuffled ? ShuffledModuleJson(index, expression) : OrderedModuleJson(index, expression);

    private static string OrderedModuleJson(int index, string expression)
        => $$"""
        {
          "id": "fuzz-breadth-{{index}}",
          "version": "1.0.0",
          "functions": [{{FunctionJson(expression, shuffled: false)}}]
        }
        """;

    private static string ShuffledModuleJson(int index, string expression)
        => $$"""
        {
          "functions": [{{FunctionJson(expression, shuffled: true)}}],
          "version": "1.0.0",
          "id": "fuzz-breadth-{{index}}"
        }
        """;

    private static string FunctionJson(string expression, bool shuffled)
        => shuffled ? $$"""
        {
          "body": [{ "op": "return", "value": {{expression}} }],
          "returnType": "I32",
          "parameters": [
            { "type": "I32", "name": "a" },
            { "type": "I32", "name": "b" },
            { "type": "I32", "name": "c" },
            { "type": "I32", "name": "value" }
          ],
          "visibility": "entrypoint",
          "id": "main"
        }
        """ : $$"""
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [
            { "name": "a", "type": "I32" },
            { "name": "b", "type": "I32" },
            { "name": "c", "type": "I32" },
            { "name": "value", "type": "I32" }
          ],
          "returnType": "I32",
          "body": [{ "op": "return", "value": {{expression}} }]
        }
        """;

    private static string Token(Random random)
    {
        var bytes = new byte[4];
        random.NextBytes(bytes);
        return Convert.ToHexString(bytes) +
               random.Next(0, 1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ArtifactManifest Manifest(byte[] bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new ArtifactManifest(1, "fuzz", "module", "plan", "policy", "bindings",
            "runtime", "compiler", "types", "effects", "verifier", "1.0.0", "net10.0", [], hash, DateTimeOffset.UtcNow);
    }
}

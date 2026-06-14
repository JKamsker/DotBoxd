using DotBoxd.Kernels;

namespace DotBoxd.Kernels.Tests;

public sealed class ValidationHardeningTests
{
    [Fact]
    public async Task Unreachable_tail_statements_are_still_validated()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "unreachable-tail",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "return", "value": { "i32": 1 } },
                { "op": "expr", "value": { "call": "host.unknown", "args": [] } }
              ]
            }
          ]
        }
        """);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-CALL-UNKNOWN");
    }

    [Fact]
    public async Task Nested_unreachable_tail_statements_are_still_validated()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "nested-unreachable-tail",
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
                  "then": [
                    { "op": "return", "value": { "i32": 1 } },
                    { "op": "expr", "value": { "call": "host.unknown", "args": [] } }
                  ],
                  "else": [
                    { "op": "return", "value": { "i32": 2 } }
                  ]
                }
              ]
            }
          ]
        }
        """);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-CALL-UNKNOWN");
    }

    [Fact]
    public async Task Non_collection_calls_reject_generic_type()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "generic-type-smuggling",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "math.abs",
                    "genericType": { "name": "System.Type" },
                    "args": [{ "i32": -1 }]
                  }
                }
              ]
            }
          ]
        }
        """);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-CALL-GENERIC");
        Assert.Contains(ex.Diagnostics, d => d.Code == "E-TYPE-UNKNOWN");
    }

    [Fact]
    public async Task Non_collection_calls_reject_known_generic_type()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "known-generic-type-smuggling",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "math.abs",
                    "genericType": "I32",
                    "args": [{ "i32": -1 }]
                  }
                }
              ]
            }
          ]
        }
        """);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-CALL-GENERIC");
        Assert.DoesNotContain(ex.Diagnostics, d => d.Code == "E-TYPE-UNKNOWN");
    }

    [Fact]
    public async Task Collection_calls_reject_generic_type_except_empty_factories()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "collection-generic-type-smuggling",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": { "name": "List", "arguments": ["I32"] },
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "list.of",
                    "genericType": "I32",
                    "args": [{ "i32": 1 }]
                  }
                }
              ]
            }
          ]
        }
        """);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-CALL-GENERIC");
    }

    [Fact]
    public async Task Valid_generic_sites_reject_forbidden_generic_type()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "generic-site-forbidden-type",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": { "name": "List", "arguments": ["System.Type"] },
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "list.empty",
                    "genericType": { "name": "System.Type" },
                    "args": []
                  }
                }
              ]
            }
          ]
        }
        """);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-TYPE-UNKNOWN");
    }

    [Fact]
    public async Task Deterministic_policy_denies_file_io_effects()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson("config.json"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileRead(Path.GetTempPath(), 1024)
            .Deterministic(DateTimeOffset.UnixEpoch, randomSeed: 1)
            .Build();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-DETERMINISM");
    }

    [Fact]
    public async Task Deterministic_policy_denies_external_allowed_effects_even_for_pure_module()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileRead(Path.GetTempPath(), 1024)
            .Deterministic(DateTimeOffset.UnixEpoch, randomSeed: 1)
            .Build();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-DETERMINISM");
    }

    [Fact]
    public async Task Policy_with_unknown_effect_bits_is_rejected()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var policy = new SandboxPolicy(
            "unknown-effects",
            SandboxEffects.Pure | (SandboxEffect)(1 << 20),
            [],
            new ResourceLimits(MaxFuel: 1_000));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-EFFECT");
    }
}

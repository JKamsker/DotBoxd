using SafeIR;

namespace SafeIR.Tests;

public sealed class PolicyBoundaryTests
{
    [Fact]
    public async Task Expired_grant_parameters_are_not_used_at_runtime()
    {
        using var expiredRoot = TempDirectory.Create();
        using var activeRoot = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(expiredRoot.Path, "secret.txt"), "expired-secret");
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(InterpreterAndPolicyTests.FileReadJson("secret.txt"));
        var policy = new SandboxPolicy(
            "grant-expiry",
            SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.FileRead,
            [
                FileReadGrant(expiredRoot.Path, DateTimeOffset.UtcNow.AddDays(-1)),
                FileReadGrant(activeRoot.Path, DateTimeOffset.UtcNow.AddDays(1))
            ],
            new ResourceLimits(MaxFuel: 5_000));
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.NotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Prepare_rejects_direct_policy_with_negative_resource_limit()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var policy = new SandboxPolicy(
            "invalid-limits",
            SandboxEffects.Pure,
            [],
            new ResourceLimits(MaxFuel: -1));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-LIMIT");
    }

    [Fact]
    public async Task Prepare_rejects_duplicate_active_capability_grants()
    {
        using var first = TempDirectory.Create();
        using var second = TempDirectory.Create();
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(InterpreterAndPolicyTests.FileReadJson("config.json"));
        var policy = new SandboxPolicy(
            "duplicate-grants",
            SandboxEffects.Pure | SandboxEffect.FileRead,
            [
                FileReadGrant(first.Path, DateTimeOffset.UtcNow.AddDays(1)),
                FileReadGrant(second.Path, DateTimeOffset.UtcNow.AddDays(1))
            ],
            new ResourceLimits(MaxFuel: 5_000));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-GRANT");
    }

    [Fact]
    public async Task Prepare_rejects_unknown_grant_not_required_by_registered_binding()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var policy = new SandboxPolicy(
            "unknown-grant",
            SandboxEffects.Pure,
            [new CapabilityGrant("vendor.secret", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 5_000));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-GRANT");
    }

    [Fact]
    public async Task Unknown_capability_request_cannot_be_satisfied_by_unknown_grant()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(UnknownCapabilityRequestJson());
        var policy = new SandboxPolicy(
            "unknown-request",
            SandboxEffects.Pure,
            [new CapabilityGrant("vendor.secret", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 5_000));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-GRANT");
    }

    [Theory]
    [InlineData("maxBytesPerRun", "not-a-number")]
    [InlineData("allowOverwrite", "maybe")]
    [InlineData("unknown", "value")]
    public async Task Prepare_rejects_malformed_file_grant_parameters(string key, string value)
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(FileWriteJson("out.txt", "x"));
        var parameters = new Dictionary<string, string> {
            ["root"] = temp.Path,
            ["maxBytesPerRun"] = "1024",
            ["allowCreate"] = "true",
            ["allowOverwrite"] = "true",
            [key] = value
        };
        var policy = new SandboxPolicy(
            "bad-file-grant",
            SandboxEffects.Pure | SandboxEffect.FileWrite | SandboxEffect.Audit,
            [new CapabilityGrant("file.write", parameters)],
            new ResourceLimits(MaxFuel: 5_000, MaxFileBytesWritten: 1024));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-GRANT-PARAM");
    }

    [Fact]
    public void Policy_snapshots_grant_parameter_dictionaries()
    {
        var parameters = new Dictionary<string, string> {
            ["root"] = "original",
            ["maxBytesPerRun"] = "1"
        };

        var policy = new SandboxPolicy(
            "snapshot",
            SandboxEffects.Pure | SandboxEffect.FileRead,
            [new CapabilityGrant("file.read", parameters)],
            new ResourceLimits());
        parameters["root"] = "changed";

        Assert.Equal("original", policy.Grants[0].Parameters["root"]);
    }

    [Fact]
    public async Task Execute_rejects_tampered_unverified_plan()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var unverifiedModule = await host.ParseJsonAsync(UnknownCallJson());
        var tampered = plan with { Module = unverifiedModule };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.ExecuteAsync(
                tampered,
                "main",
                SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)])));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-CALL-UNKNOWN");
    }

    private static CapabilityGrant FileReadGrant(string root, DateTimeOffset expiresAt)
        => new(
            "file.read",
            new Dictionary<string, string> {
                ["root"] = root,
                ["maxBytesPerRun"] = "1024"
            },
            expiresAt);

    private static string FileWriteJson(string path, string text)
        => $$"""
        {
          "id": "policy-file-writer",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "file.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "file.writeText",
                    "args": [
                      { "path": "{{path}}" },
                      { "string": "{{text}}" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static string UnknownCallJson()
        => """
        {
          "id": "unverified-plan",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [{ "op": "return", "value": { "call": "host.unknown", "args": [] } }]
            }
          ]
        }
        """;

    private static string UnknownCapabilityRequestJson()
        => """
        {
          "id": "unknown-capability-request",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "vendor.secret" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                { "name": "level", "type": "I32" },
                { "name": "rarity", "type": "I32" }
              ],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """;

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "safe-ir-policy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

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

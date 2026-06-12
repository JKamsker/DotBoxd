using SafeIR;

namespace SafeIR.Tests;

public sealed class CapabilityRevocationTests
{
    [Fact]
    public async Task Revoked_capability_denies_prepared_interpreted_plan()
    {
        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "config.json"), "secret");
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(InterpreterAndPolicyTests.FileReadJson("config.json"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantFileRead(temp.Path, maxBytesPerRun: 1024)
                .WithFuel(1_000)
                .Build());

        host.RevokeCapability("file.read", "tenant disabled file reads");
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        AssertRevoked(result, ExecutionMode.Interpreted, "file.read", "tenant disabled file reads");
    }

    [Fact]
    public async Task Revoked_capability_denies_prepared_plan_before_compiled_cache_reuse()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ParseJsonAsync(PureModuleWithLoggingRequest());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantLogging()
                .WithFuel(1_000)
                .Build());
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Compiled,
            AllowFallbackToInterpreter = false
        };

        var first = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, options);
        host.RevokeCapability("log.write", "audit channel disabled");
        var second = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, options);

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.NotNull(first.ArtifactHash);
        AssertRevoked(second, ExecutionMode.Compiled, "log.write", "audit channel disabled");
        Assert.Null(second.ArtifactHash);
        Assert.DoesNotContain(second.AuditEvents, e =>
            e.Kind == "RunSummary" &&
            e.Message?.Contains("cacheStatus=Hit", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Revoked_unrelated_capability_does_not_deny_execution()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        host.RevokeCapability("log.write", "unrelated");
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "CapabilityRevoked");
    }

    private static void AssertRevoked(
        SandboxExecutionResult result,
        ExecutionMode expectedMode,
        string capabilityId,
        string reason)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, result.Error!.Code);
        Assert.Equal(expectedMode, result.ActualMode);
        var audit = Assert.Single(result.AuditEvents, e => e.Kind == "CapabilityRevoked");
        Assert.False(audit.Success);
        Assert.Equal(capabilityId, audit.CapabilityId);
        Assert.Equal(reason, audit.Message);
        Assert.Equal(reason, audit.Fields!["reason"]);
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "RunSummary" &&
            !e.Success &&
            e.ErrorCode == SandboxErrorCode.PolicyDenied);
    }

    private static string PureModuleWithLoggingRequest()
        => """
        {
          "id": "log-requested-pure",
          "version": "1.0.0",
          "capabilityRequests": [
            { "id": "log.write", "reason": "audit messages" }
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
        """;

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "safe-ir-revocation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Fuzz;

public sealed class GoldenCorpusTests
{
    [Theory]
    [InlineData("pure-score", "e37b572baf912b9f8f683fab6ec2bdfcda9ab1564d83964d06944847be8eace4")]
    [InlineData("file-read", "2556a900395d83c3d5cc195c6a6ffe80751ceaa73c5f5ae92ec389470a8a882f")]
    public void Golden_modules_preserve_canonical_hashes(string name, string expectedHash)
    {
        var module = JsonImporter.Import(GoldenJson(name));

        Assert.Equal(expectedHash, CanonicalModuleHasher.Hash(module));
    }

    [Fact]
    public async Task Golden_pure_score_result_is_stable_across_backends()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(GoldenJson("pure-score"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(10_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(3), SandboxValue.FromInt32(4)]);

        var interpreted = await ExecuteAsync(host, plan, input, ExecutionMode.Interpreted);
        var compiled = await ExecuteAsync(host, plan, input, ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(34, ((I32Value)interpreted.Value!).Value);
        Assert.Equal(34, ((I32Value)compiled.Value!).Value);
        Assert.Equal(9, interpreted.ResourceUsage.FuelUsed);
        Assert.Equal(10, compiled.ResourceUsage.FuelUsed);
    }

    [Fact]
    public async Task Golden_file_read_behavior_is_stable()
    {
        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "settings.json"), "golden-settings");
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(GoldenJson("file-read"));

        var denied = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(10_000).Build()));
        Assert.Contains(denied.Diagnostics, d => d.Code == "E-POLICY-CAP");

        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .AllowRuntimeAsync()
                .GrantFileRead(temp.Path, 1024)
                .WithWallTime(TimeSpan.FromSeconds(2))
                .WithFuel(10_000)
                .Build());

        Assert.Equal(
            SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.FileRead | SandboxEffect.Concurrency,
            plan.FunctionAnalysis["main"].Effects);

        var result = await ExecuteAsync(host, plan, SandboxValue.Unit, ExecutionMode.Interpreted);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("golden-settings", ((StringValue)result.Value!).Value);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Equal(69, result.ResourceUsage.FuelUsed);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
        Assert.Equal(15, result.ResourceUsage.FileBytesRead);
        var audit = Assert.Single(result.AuditEvents, e => e.BindingId == "file.readText" && e.Success);
        Assert.Equal("15", audit.Fields!["bytesRead"]);
    }

    private static ValueTask<SandboxExecutionResult> ExecuteAsync(
        SandboxHost host,
        ExecutionPlan plan,
        SandboxValue input,
        ExecutionMode mode)
        => host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

    private static string GoldenJson(string name)
        => name switch
        {
            "pure-score" => """
            {
              "id": "golden-score",
              "version": "1.0.0",
              "targetSandboxVersion": "1.0.0",
              "functions": [
                {
                  "id": "main",
                  "visibility": "entrypoint",
                  "parameters": [
                    { "name": "level", "type": "I32" },
                    { "name": "rarity", "type": "I32" }
                  ],
                  "returnType": "I32",
                  "body": [
                    {
                      "op": "set",
                      "name": "base",
                      "value": { "op": "mul", "left": { "var": "level" }, "right": { "i32": 10 } }
                    },
                    {
                      "op": "return",
                      "value": { "op": "add", "left": { "var": "base" }, "right": { "var": "rarity" } }
                    }
                  ]
                }
              ]
            }
            """,
            "file-read" => """
            {
              "id": "golden-file-read",
              "version": "1.0.0",
              "capabilityRequests": [{ "id": "file.read", "reason": "golden" }],
              "functions": [
                {
                  "id": "main",
                  "visibility": "entrypoint",
                  "parameters": [],
                  "returnType": "String",
                  "body": [
                    {
                      "op": "return",
                      "value": { "call": "file.readText", "args": [{ "path": "settings.json" }] }
                    }
                  ]
                }
              ]
            }
            """,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
        };

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotboxd-golden-" + Guid.NewGuid().ToString("N"));
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

using DotBoxd.Kernels.Compiler;
using DotBoxd.Hosting;
using DotBoxd.Kernels.Verifier;
using System.Text.Json;

namespace DotBoxd.Kernels.Tests;

public sealed class CompiledCacheAuditTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [Fact]
    public async Task Invalid_cached_entry_emits_structured_cache_audit()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);
        _ = await ExecuteCompiled(host, plan, input);
        await ReplaceManifestAsync(CacheEntry(temp.Path, plan), m => m with { VerifierVersion = "stale" });

        var result = await ExecuteCompiled(host, plan, input);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var invalidated = Assert.Single(result.AuditEvents, e => e.Kind == "CacheInvalidated");
        Assert.False(invalidated.Success);
        Assert.Equal(SandboxErrorCode.CacheInvalid, invalidated.ErrorCode);
        Assert.Equal("cache:" + CacheKey(plan), invalidated.ResourceId);
        Assert.Equal(plan.PlanHash, invalidated.Fields!["planHash"]);
        Assert.Equal(SandboxErrorCode.CacheInvalid.ToString(), invalidated.Fields["reason"]);
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.Equal("Recompiled", summary.Fields!["cacheStatus"]);
    }

    private static async ValueTask<SandboxExecutionResult> ExecuteCompiled(
        SandboxHost host,
        ExecutionPlan plan,
        SandboxValue input)
        => await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

    private static string CacheEntry(string root, ExecutionPlan plan)
    {
        var key = CacheKey(plan);
        return Path.Combine(root, key[..2], key[2..4], key);
    }

    private static string CacheKey(ExecutionPlan plan)
        => CacheKeyBuilder.Build(plan, "main", VerificationPolicy.BoxedValueDefaults(), optimize: false);

    private static async Task ReplaceManifestAsync(
        string entryPath,
        Func<ArtifactManifest, ArtifactManifest> replace)
    {
        var path = Path.Combine(entryPath, "manifest.json");
        ArtifactManifest manifest;
        await using (var read = File.OpenRead(path))
        {
            manifest = await JsonSerializer.DeserializeAsync<ArtifactManifest>(read, JsonOptions) ??
                throw new JsonException("empty manifest");
        }

        await using var write = File.Create(path);
        await JsonSerializer.SerializeAsync(write, replace(manifest), JsonOptions);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotboxd-cache-" + Guid.NewGuid().ToString("N"));
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

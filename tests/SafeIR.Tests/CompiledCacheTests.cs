using SafeIR;
using SafeIR.Compiler;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class CompiledCacheTests
{
    [Fact]
    public async Task Compiled_artifact_is_persisted_and_reused()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);

        var first = await ExecuteCompiled(host, plan, input);
        var second = await ExecuteCompiled(host, plan, input);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Contains(first.AuditEvents, e => e.Message?.Contains("cacheStatus=Miss", StringComparison.Ordinal) == true);
        Assert.Contains(second.AuditEvents, e => e.Message?.Contains("cacheStatus=Hit", StringComparison.Ordinal) == true);
        Assert.True(File.Exists(Path.Combine(CacheEntry(temp.Path, plan), "module.dll")));
        Assert.True(File.Exists(Path.Combine(CacheEntry(temp.Path, plan), "manifest.json")));
        Assert.True(File.Exists(Path.Combine(CacheEntry(temp.Path, plan), "verification.json")));
    }

    [Fact]
    public async Task Policy_hash_change_uses_a_different_cache_key()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var firstPlan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var secondPlan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(2_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);

        _ = await ExecuteCompiled(host, firstPlan, input);
        _ = await ExecuteCompiled(host, secondPlan, input);

        Assert.NotEqual(CacheKey(firstPlan), CacheKey(secondPlan));
        Assert.True(Directory.Exists(CacheEntry(temp.Path, firstPlan)));
        Assert.True(Directory.Exists(CacheEntry(temp.Path, secondPlan)));
    }

    [Fact]
    public async Task Corrupted_cached_dll_is_quarantined_and_recompiled()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);
        _ = await ExecuteCompiled(host, plan, input);
        await File.WriteAllBytesAsync(Path.Combine(CacheEntry(temp.Path, plan), "module.dll"), [1, 2, 3, 4]);

        var result = await ExecuteCompiled(host, plan, input);

        Assert.True(result.Succeeded);
        Assert.Contains(result.AuditEvents, e => e.Message?.Contains("cacheStatus=Recompiled", StringComparison.Ordinal) == true);
        Assert.True(Directory.Exists(Path.Combine(temp.Path, "quarantine")));
        Assert.NotEmpty(Directory.GetDirectories(Path.Combine(temp.Path, "quarantine")));
    }

    private static async ValueTask<SandboxExecutionResult> ExecuteCompiled(
        Hosting.SandboxHost host,
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
        => CacheKeyBuilder.Build(plan, VerificationPolicy.BoxedValueDefaults(), optimize: false);

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "safe-ir-cache-" + Guid.NewGuid().ToString("N"));
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

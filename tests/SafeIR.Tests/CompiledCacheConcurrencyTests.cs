using SafeIR.Compiler;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class CompiledCacheConcurrencyTests
{
    [Fact]
    public async Task Same_key_cold_compiles_publish_one_valid_cache_entry()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => ExecuteCompiled(host, plan, input)));

        Assert.All(results, AssertSuccessfulCompiledResult);
        Assert.Single(Directory.EnumerateFiles(temp.Path, "module.dll", SearchOption.AllDirectories));
        Assert.True(File.Exists(Path.Combine(CacheEntry(temp.Path, plan), "manifest.json")));
        Assert.Contains(
            results.SelectMany(r => r.AuditEvents),
            e => e.Message?.Contains("cacheStatus=Miss", StringComparison.Ordinal) == true ||
                 e.Message?.Contains("cacheStatus=Recompiled", StringComparison.Ordinal) == true ||
                 e.Message?.Contains("cacheStatus=Hit", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Concurrent_reads_of_corrupted_entry_recover_without_quarantine_collisions()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);
        _ = await ExecuteCompiled(host, plan, input);
        await File.WriteAllTextAsync(Path.Combine(CacheEntry(temp.Path, plan), "manifest.json"), "{ broken json");

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => ExecuteCompiled(host, plan, input)));

        Assert.All(results, AssertSuccessfulCompiledResult);
        Assert.True(File.Exists(Path.Combine(CacheEntry(temp.Path, plan), "module.dll")));
        var quarantineRoot = Path.Combine(temp.Path, "quarantine");
        Assert.True(Directory.Exists(quarantineRoot));
        var quarantinedEntries = Directory.GetDirectories(quarantineRoot);
        Assert.NotEmpty(quarantinedEntries);
        Assert.Equal(quarantinedEntries.Length, quarantinedEntries.Select(Path.GetFileName).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Same_root_hosts_coordinate_same_key_cold_compiles()
    {
        using var temp = TempDirectory.Create();
        var hosts = Enumerable.Range(0, 6)
            .Select(_ => SandboxTestHost.Create(compiler: true, compilerCache: temp.Path))
            .ToArray();
        var module = await hosts[0].ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plans = await Task.WhenAll(hosts.Select(host =>
            host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build()).AsTask()));
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);

        var results = await Task.WhenAll(hosts.Select((host, index) => ExecuteCompiled(host, plans[index], input)));

        Assert.All(results, AssertSuccessfulCompiledResult);
        Assert.Single(Directory.EnumerateFiles(temp.Path, "module.dll", SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(temp.Path, ".locks"), "*.lock", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Same_root_hosts_coordinate_corrupted_entry_recovery()
    {
        using var temp = TempDirectory.Create();
        var seedHost = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await seedHost.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var seedPlan = await seedHost.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);
        _ = await ExecuteCompiled(seedHost, seedPlan, input);
        await File.WriteAllTextAsync(Path.Combine(CacheEntry(temp.Path, seedPlan), "manifest.json"), "{ broken json");
        var hosts = Enumerable.Range(0, 6)
            .Select(_ => SandboxTestHost.Create(compiler: true, compilerCache: temp.Path))
            .ToArray();
        var plans = await Task.WhenAll(hosts.Select(host =>
            host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build()).AsTask()));

        var results = await Task.WhenAll(hosts.Select((host, index) => ExecuteCompiled(host, plans[index], input)));

        Assert.All(results, AssertSuccessfulCompiledResult);
        Assert.True(File.Exists(Path.Combine(CacheEntry(temp.Path, seedPlan), "module.dll")));
        Assert.NotEmpty(Directory.GetDirectories(Path.Combine(temp.Path, "quarantine")));
    }

    private static async Task<SandboxExecutionResult> ExecuteCompiled(
        Hosting.SandboxHost host,
        ExecutionPlan plan,
        SandboxValue input)
        => await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

    private static void AssertSuccessfulCompiledResult(SandboxExecutionResult result)
    {
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(SandboxValue.FromInt32(45), result.Value);
    }

    private static string CacheEntry(string root, ExecutionPlan plan)
    {
        var key = CacheKeyBuilder.Build(plan, "main", VerificationPolicy.BoxedValueDefaults(), optimize: false);
        return Path.Combine(root, key[..2], key[2..4], key);
    }

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

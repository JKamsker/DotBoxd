using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier;
using PersistentCompiledArtifactCache = DotBoxD.Kernels.Compiler.PersistentCompiledArtifactCache;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

public sealed class CompiledCacheRootGuardTests
{
    [Fact]
    public void Persistent_cache_accepts_private_temp_root()
    {
        using var temp = TempDirectory.Create();

        var cache = new PersistentCompiledArtifactCache(temp.Path);

        Assert.False((bool)cache.EntryExists(new string('0', 64)));
    }

    [Fact]
    public void Persistent_cache_rejects_group_or_world_writable_unix_root()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TempDirectory.Create();
        File.SetUnixFileMode(
            temp.Path,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupWrite);

        var ex = Assert.Throws<SandboxRuntimeException>(() => new PersistentCompiledArtifactCache(temp.Path));

        Assert.Equal(SandboxErrorCode.PermissionDenied, ex.Error.Code);
    }

    [Fact]
    public void Persistent_cache_rejects_broad_windows_write_acl()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TempDirectory.Create();
        GrantEveryoneWrite(temp.Path);

        var ex = Assert.Throws<SandboxRuntimeException>(() => new PersistentCompiledArtifactCache(temp.Path));

        Assert.Equal(SandboxErrorCode.PermissionDenied, ex.Error.Code);
    }

    [Fact]
    public void Persistent_cache_rejects_reparse_point_entry_shards()
    {
        using var temp = TempDirectory.Create();
        using var outside = TempDirectory.Create();
        var cache = new PersistentCompiledArtifactCache(temp.Path);
        var link = Path.Combine(temp.Path, "aa");
        Assert.True(
            TryCreateDirectoryLink(link, outside.Path),
            "Unable to create a directory symbolic link or junction for the cache reparse-point test.");

        try
        {
            var ex = Assert.Throws<SandboxRuntimeException>(() => cache.EntryExists(new string('a', 64)));

            Assert.Equal(SandboxErrorCode.CacheInvalid, ex.Error.Code);
        }
        finally
        {
            TryDeleteDirectoryLink(link);
        }
    }

    [Fact]
    public async Task Persistent_cache_rejects_reparse_point_lock_directory()
    {
        using var temp = TempDirectory.Create();
        using var outside = TempDirectory.Create();
        var link = Path.Combine(temp.Path, ".locks");
        Assert.True(
            TryCreateDirectoryLink(link, outside.Path),
            "Unable to create a directory symbolic link or junction for the cache lock test.");

        try
        {
            var result = await ExecuteCompiledWithCacheAsync(temp.Path);

            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.CacheInvalid, result.Error!.Code);
        }
        finally
        {
            TryDeleteDirectoryLink(link);
        }
    }

    [Fact]
    public async Task Persistent_cache_rejects_reparse_point_quarantine_directory()
    {
        using var temp = TempDirectory.Create();
        using var outside = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);
        _ = await ExecuteCompiledAsync(host, plan, input);
        await File.WriteAllTextAsync(Path.Combine(CacheEntry(temp.Path, plan), "manifest.json"), "{ broken json");
        var link = Path.Combine(temp.Path, "quarantine");
        Assert.True(
            TryCreateDirectoryLink(link, outside.Path),
            "Unable to create a directory symbolic link or junction for the cache quarantine test.");

        try
        {
            var result = await ExecuteCompiledAsync(host, plan, input);

            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.CacheInvalid, result.Error!.Code);
            Assert.Empty(Directory.GetFileSystemEntries(outside.Path));
        }
        finally
        {
            TryDeleteDirectoryLink(link);
        }
    }

    private static async Task<SandboxExecutionResult> ExecuteCompiledWithCacheAsync(string cachePath)
    {
        var host = SandboxTestHost.Create(compiler: true, compilerCache: cachePath);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);
        return await ExecuteCompiledAsync(host, plan, input);
    }

    private static async Task<SandboxExecutionResult> ExecuteCompiledAsync(
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
        var key = CacheKeyBuilder.Build(plan, "main", VerificationPolicy.BoxedValueDefaults(), optimize: false);
        return Path.Combine(root, key[..2], key[2..4], key);
    }

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return TryCreateDirectoryJunction(link, target);
        }
        catch (UnauthorizedAccessException)
        {
            return TryCreateDirectoryJunction(link, target);
        }
        catch (PlatformNotSupportedException)
        {
            return TryCreateDirectoryJunction(link, target);
        }
    }

    private static bool TryCreateDirectoryJunction(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var process = Process.Start(new ProcessStartInfo(
            "cmd.exe",
            $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        });
        process?.WaitForExit();
        return process?.ExitCode == 0 && Directory.Exists(link);
    }

    private static void TryDeleteDirectoryLink(string link)
    {
        try
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [SupportedOSPlatform("windows")]
    private static void GrantEveryoneWrite(string path)
    {
        var info = new DirectoryInfo(path);
        var security = info.GetAccessControl(AccessControlSections.Access);
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        security.AddAccessRule(new FileSystemAccessRule(
            everyone,
            FileSystemRights.Write,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotboxd-cache-root-" + Guid.NewGuid().ToString("N"));
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

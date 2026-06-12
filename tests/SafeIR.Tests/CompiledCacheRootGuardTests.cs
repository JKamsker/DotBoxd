using System.Diagnostics;
using SafeIR.Compiler;

namespace SafeIR.Tests;

public sealed class CompiledCacheRootGuardTests
{
    [Fact]
    public void Persistent_cache_accepts_private_temp_root()
    {
        using var temp = TempDirectory.Create();

        var cache = new PersistentCompiledArtifactCache(temp.Path);

        Assert.False(cache.EntryExists(new string('0', 64)));
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

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "safe-ir-cache-root-" + Guid.NewGuid().ToString("N"));
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

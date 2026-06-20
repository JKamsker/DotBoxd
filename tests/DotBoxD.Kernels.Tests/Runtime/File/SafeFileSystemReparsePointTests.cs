using System.Diagnostics;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Interpreter;
using SafeFileSystem = DotBoxD.Kernels.Runtime.Bindings.FileSystem.SafeFileSystem;

namespace DotBoxD.Kernels.Tests.Runtime.File;

public sealed class SafeFileSystemReparsePointTests
{
    [Fact]
    public async Task File_read_denies_nested_reparse_point()
    {
        using var root = TempDirectory.Create();
        using var outside = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(outside.Path, "sub"));
        await System.IO.File.WriteAllTextAsync(Path.Combine(outside.Path, "sub", "secret.txt"), "secret");
        var link = Path.Combine(root.Path, "link");
        Assert.True(
            TryCreateDirectoryLink(link, outside.Path),
            "Unable to create a directory symbolic link or junction for the reparse-point test.");

        try
        {
            var result = await ExecuteReadAsync(root.Path, "link/sub/secret.txt");

            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        }
        finally
        {
            TryDeleteDirectoryLink(link);
        }
    }

    [Fact]
    public async Task File_read_denies_terminal_reparse_point()
    {
        using var root = TempDirectory.Create();
        using var outside = TempDirectory.Create();
        var secret = Path.Combine(outside.Path, "secret.txt");
        await System.IO.File.WriteAllTextAsync(secret, "secret");
        var link = Path.Combine(root.Path, "secret.txt");
        var directoryLink = false;
        if (!TryCreateFileLink(link, secret))
        {
            directoryLink = true;
            Assert.True(
                TryCreateDirectoryLink(link, outside.Path),
                "Unable to create a terminal file symlink or directory reparse point for the test.");
        }

        try
        {
            var result = await ExecuteReadAsync(root.Path, "secret.txt");

            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        }
        finally
        {
            if (directoryLink)
            {
                TryDeleteDirectoryLink(link);
            }
            else
            {
                TryDeleteFileLink(link);
            }
        }
    }

    [Fact]
    public async Task File_write_denies_nested_reparse_point()
    {
        using var root = TempDirectory.Create();
        using var outside = TempDirectory.Create();
        var outsideFile = Path.Combine(outside.Path, "secret.txt");
        await System.IO.File.WriteAllTextAsync(outsideFile, "original");
        var link = Path.Combine(root.Path, "link");
        Assert.True(
            TryCreateDirectoryLink(link, outside.Path),
            "Unable to create a directory symbolic link or junction for the reparse-point test.");

        try
        {
            var result = await ExecuteWriteAsync(root.Path, "link/secret.txt", "new");

            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
            Assert.Equal("original", await System.IO.File.ReadAllTextAsync(outsideFile));
        }
        finally
        {
            TryDeleteDirectoryLink(link);
        }
    }

    [Fact]
    public async Task File_write_denies_terminal_reparse_point()
    {
        using var root = TempDirectory.Create();
        using var outside = TempDirectory.Create();
        var outsideFile = Path.Combine(outside.Path, "secret.txt");
        await System.IO.File.WriteAllTextAsync(outsideFile, "original");
        var link = Path.Combine(root.Path, "secret.txt");
        var directoryLink = false;
        if (!TryCreateFileLink(link, outsideFile))
        {
            directoryLink = true;
            Assert.True(
                TryCreateDirectoryLink(link, outside.Path),
                "Unable to create a terminal file symlink or directory reparse point for the test.");
        }

        try
        {
            var result = await ExecuteWriteAsync(root.Path, "secret.txt", "new");

            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
            Assert.Equal("original", await System.IO.File.ReadAllTextAsync(outsideFile));
        }
        finally
        {
            if (directoryLink)
            {
                TryDeleteDirectoryLink(link);
            }
            else
            {
                TryDeleteFileLink(link);
            }
        }
    }

    [Fact]
    public async Task File_write_denies_precreated_temp_reparse_point()
    {
        using var root = TempDirectory.Create();
        using var outside = TempDirectory.Create();
        var outsideFile = Path.Combine(outside.Path, "secret.txt");
        await System.IO.File.WriteAllTextAsync(outsideFile, "original");
        var tempLink = Path.Combine(root.Path, "target.txt.tmp-known");
        var directoryLink = false;
        if (!TryCreateFileLink(tempLink, outsideFile))
        {
            directoryLink = true;
            Assert.True(
                TryCreateDirectoryLink(tempLink, outside.Path),
                "Unable to create a temp file symlink or directory reparse point for the test.");
        }

        try
        {
            using var _ = SafeFileSystem.UseTempSuffixForTests("known");
            var result = await ExecuteWriteAsync(root.Path, "target.txt", "new");

            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
            Assert.Equal("original", await System.IO.File.ReadAllTextAsync(outsideFile));
            Assert.False(System.IO.File.Exists(Path.Combine(root.Path, "target.txt")));
        }
        finally
        {
            if (directoryLink)
            {
                TryDeleteDirectoryLink(tempLink);
            }
            else
            {
                TryDeleteFileLink(tempLink);
            }
        }
    }

    private static async Task<SandboxExecutionResult> ExecuteReadAsync(string root, string path)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson(path));
        var policy = FilePolicyBuilder()
            .GrantFileRead(root, 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
    }

    private static async Task<SandboxExecutionResult> ExecuteWriteAsync(string root, string path, string text)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(FileWriteJson(path, text));
        var policy = FilePolicyBuilder()
            .GrantFileWrite(root, 1024, allowCreate: true, allowOverwrite: true)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
    }

    private static string FileWriteJson(string path, string text)
        => $$"""
        {
          "id": "file-writer",
          "version": "1.0.0",
          "capabilityRequests": [
            { "id": "file.write", "reason": "test write" }
          ],
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
                      { "path": "{{path.Replace("\\", "\\\\", StringComparison.Ordinal)}}" },
                      { "string": "{{text}}" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static SandboxPolicyBuilder FilePolicyBuilder()
        => SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .WithWallTime(TimeSpan.FromSeconds(2));

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

    private static bool TryCreateFileLink(string link, string target)
    {
        try
        {
            System.IO.File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
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

    private static void TryDeleteFileLink(string link)
    {
        try
        {
            if (System.IO.File.Exists(link))
            {
                System.IO.File.Delete(link);
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
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotboxd-" + Guid.NewGuid().ToString("N"));
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

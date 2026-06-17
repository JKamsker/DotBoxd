using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Interpreter;
using RuntimeSafeFileSystem = DotBoxD.Kernels.Runtime.Bindings.SafeFileSystem;

namespace DotBoxD.Kernels.Tests.Runtime.File;

public sealed class SafeFileSystemDriveRootTests
{
    [Fact]
    public async Task File_read_allows_windows_drive_root_grant()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TempDirectory.Create();
        var file = Path.Combine(temp.Path, "settings.json");
        await System.IO.File.WriteAllTextAsync(file, "ok");
        var root = Path.GetPathRoot(temp.Path)!;
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson(relative));
        var policy = SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .GrantFileRead(root, 1024)
            .WithFuel(5_000)
            .WithWallTime(TimeSpan.FromSeconds(2))
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("ok", ((StringValue)result.Value!).Value);
    }

    [Fact]
    public void File_write_parent_directory_allows_windows_drive_root_grant()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TempDirectory.Create();
        var root = Path.GetPathRoot(temp.Path)!;
        var rootFull = EnsureTrailingSeparator(Path.GetFullPath(root));
        var fullPath = Path.Combine(temp.Path, "out.txt");

        SafeFileWritePublisher.EnsureParentDirectory(
            rootFull,
            fullPath,
            new SafeFileWritePermission(AllowCreate: true, AllowOverwrite: false));
        RuntimeSafeFileSystem.EnsureNoReparsePoint(rootFull, fullPath);
    }

    private static string EnsureTrailingSeparator(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotboxd-drive-root-" + Guid.NewGuid().ToString("N"));
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

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using SafeIR;
using SafeIR.Compiler.Internal;

namespace SafeIR.Tests;

/// <summary>
/// COR-0058: the compiled-cache HMAC origin key is a host secret. Its directory
/// and key file must be created owner-only, and reads must fail closed when the
/// directory/file grants group/world or broad-principal access. These tests mirror
/// <c>CompiledCacheRootGuardTests</c> for the origin-key trust root.
/// </summary>
public sealed class Fix_COR_0058_Tests
{
    [Fact]
    public void Hardened_directory_passes_validation()
    {
        using var temp = TempDir.Create();
        var dir = Path.Combine(temp.Path, "SafeIR");
        Directory.CreateDirectory(dir);

        PersistentCompiledArtifactCacheOriginKeyGuard.HardenDirectory(dir);

        // Hardening then validating its own result must not throw.
        PersistentCompiledArtifactCacheOriginKeyGuard.ValidateDirectory(dir);
    }

    [Fact]
    public void Hardened_key_file_passes_validation()
    {
        using var temp = TempDir.Create();
        var file = Path.Combine(temp.Path, "compiled-cache-origin.key");
        File.WriteAllBytes(file, new byte[32]);

        PersistentCompiledArtifactCacheOriginKeyGuard.HardenFile(file);

        PersistentCompiledArtifactCacheOriginKeyGuard.ValidateFile(file);
    }

    [Fact]
    public void Validation_rejects_group_or_world_readable_unix_key_file()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TempDir.Create();
        var file = Path.Combine(temp.Path, "compiled-cache-origin.key");
        File.WriteAllBytes(file, new byte[32]);
        File.SetUnixFileMode(
            file,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);

        var ex = Assert.Throws<SandboxRuntimeException>(
            () => PersistentCompiledArtifactCacheOriginKeyGuard.ValidateFile(file));

        Assert.Equal(SandboxErrorCode.PermissionDenied, ex.Error.Code);
    }

    [Fact]
    public void Validation_rejects_group_or_world_writable_unix_directory()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TempDir.Create();
        var dir = Path.Combine(temp.Path, "SafeIR");
        Directory.CreateDirectory(dir);
        File.SetUnixFileMode(
            dir,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute |
            UnixFileMode.OtherWrite);

        var ex = Assert.Throws<SandboxRuntimeException>(
            () => PersistentCompiledArtifactCacheOriginKeyGuard.ValidateDirectory(dir));

        Assert.Equal(SandboxErrorCode.PermissionDenied, ex.Error.Code);
    }

    [Fact]
    public void Validation_rejects_broad_windows_acl_on_key_file()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TempDir.Create();
        var file = Path.Combine(temp.Path, "compiled-cache-origin.key");
        File.WriteAllBytes(file, new byte[32]);
        GrantEveryoneRead(file);

        var ex = Assert.Throws<SandboxRuntimeException>(
            () => PersistentCompiledArtifactCacheOriginKeyGuard.ValidateFile(file));

        Assert.Equal(SandboxErrorCode.PermissionDenied, ex.Error.Code);
    }

    [Fact]
    public void Hardening_strips_broad_windows_acl_from_attacker_created_key_file()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TempDir.Create();
        var file = Path.Combine(temp.Path, "compiled-cache-origin.key");
        File.WriteAllBytes(file, new byte[32]);
        GrantEveryoneRead(file);

        // Hardening must remove the broad principal so the file then validates clean.
        PersistentCompiledArtifactCacheOriginKeyGuard.HardenFile(file);

        PersistentCompiledArtifactCacheOriginKeyGuard.ValidateFile(file);
    }

    [SupportedOSPlatform("windows")]
    private static void GrantEveryoneRead(string filePath)
    {
        var info = new FileInfo(filePath);
        var security = info.GetAccessControl(AccessControlSections.Access);
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        security.AddAccessRule(new FileSystemAccessRule(
            everyone,
            FileSystemRights.Read,
            AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    private sealed class TempDir : IDisposable
    {
        private TempDir(string path) => Path = path;

        public string Path { get; }

        public static TempDir Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "safe-ir-origin-key-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDir(path);
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

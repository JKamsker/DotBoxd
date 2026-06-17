using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime.Bindings;

internal static class SafeFileWritePublisher
{
    private static readonly object[] PublishLocks = CreatePublishLocks();

    public static SafeFileWritePermission EnsureAllowed(
        CapabilityGrant grant,
        string fullPath,
        long byteCount)
    {
        var options = SafeFileGrantReader.Read(grant);
        var exists = File.Exists(fullPath);
        if (exists && !options.AllowOverwrite) {
            throw Error(SandboxErrorCode.PermissionDenied, "file.writeText denied: overwrite is not allowed");
        }

        if (!exists && !options.AllowCreate) {
            throw Error(SandboxErrorCode.PermissionDenied, "file.writeText denied: create is not allowed");
        }

        var maxBytes = options.MaxBytesPerRun ?? long.MaxValue;
        if (byteCount > maxBytes) {
            throw Error(SandboxErrorCode.QuotaExceeded, "file.writeText denied: content exceeds write limit");
        }

        return new SafeFileWritePermission(options.AllowCreate, options.AllowOverwrite);
    }

    public static void EnsureParentDirectory(
        string rootFull,
        string fullPath,
        SafeFileWritePermission permission)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory)) {
            return;
        }

        var root = Path.TrimEndingDirectorySeparator(rootFull);
        var relative = Path.GetRelativePath(root, directory);
        if (SafeFileSystem.IsRootEscapeRelativePath(relative)) {
            throw Error(SandboxErrorCode.PermissionDenied, "file access denied: path is outside the granted sandbox root");
        }

        if (relative is "." or "") {
            SafeFileSystem.EnsureNoReparsePoint(rootFull, fullPath);
            return;
        }

        var current = root;
        foreach (var part in relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries)) {
            current = Path.Combine(current, part);
            if (Directory.Exists(current) || File.Exists(current)) {
                SafeFileSystem.EnsureNoReparsePoint(rootFull, current);
                continue;
            }

            if (!permission.AllowCreate) {
                throw Error(SandboxErrorCode.PermissionDenied, "file.writeText denied: create is not allowed");
            }

            FileSystem.SafeFileSystem.InvokeBeforeDirectoryCreateForTests(current);
            if (Directory.Exists(current) || File.Exists(current)) {
                SafeFileSystem.EnsureNoReparsePoint(rootFull, current);
                continue;
            }

            Directory.CreateDirectory(current);
            SafeFileSystem.EnsureNoReparsePoint(rootFull, current);
        }

        if (!Directory.Exists(directory)) {
            throw Error(SandboxErrorCode.PermissionDenied, "file.writeText denied: create is not allowed");
        }

        SafeFileSystem.EnsureNoReparsePoint(rootFull, fullPath);
    }

    public static void PublishTempFile(
        string tempPath,
        string finalPath,
        SafeFileWritePermission permission)
    {
        lock (GetPublishLock(finalPath)) {
            try {
                EnsurePublishAllowed(finalPath, permission);
                if (!permission.AllowCreate) {
                    File.Replace(tempPath, finalPath, destinationBackupFileName: null);
                    return;
                }

                File.Move(tempPath, finalPath, overwrite: permission.AllowOverwrite);
            }
            catch (IOException) when (!permission.AllowOverwrite && File.Exists(finalPath)) {
                throw Error(SandboxErrorCode.PermissionDenied, "file.writeText denied: overwrite is not allowed");
            }
            catch (IOException) when (!permission.AllowCreate && !File.Exists(finalPath)) {
                throw Error(SandboxErrorCode.PermissionDenied, "file.writeText denied: create is not allowed");
            }
        }
    }

    public static void TryDelete(string path)
    {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
        catch (IOException) {
        }
        catch (UnauthorizedAccessException) {
        }
    }

    private static SandboxRuntimeException Error(SandboxErrorCode code, string message)
        => new(new SandboxError(code, message));

    private static void EnsurePublishAllowed(string finalPath, SafeFileWritePermission permission)
    {
        var exists = File.Exists(finalPath);
        if (exists && !permission.AllowOverwrite) {
            throw Error(SandboxErrorCode.PermissionDenied, "file.writeText denied: overwrite is not allowed");
        }

        if (!exists && !permission.AllowCreate) {
            throw Error(SandboxErrorCode.PermissionDenied, "file.writeText denied: create is not allowed");
        }
    }

    private static object GetPublishLock(string finalPath)
    {
        var hash = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase.GetHashCode(finalPath)
            : StringComparer.Ordinal.GetHashCode(finalPath);
        return PublishLocks[(hash & int.MaxValue) % PublishLocks.Length];
    }

    private static object[] CreatePublishLocks()
    {
        var locks = new object[64];
        for (var i = 0; i < locks.Length; i++) {
            locks[i] = new object();
        }

        return locks;
    }
}

internal sealed record SafeFileWritePermission(bool AllowCreate, bool AllowOverwrite);

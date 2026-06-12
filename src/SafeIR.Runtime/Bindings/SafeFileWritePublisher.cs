namespace SafeIR.Runtime;

using System.Globalization;
using SafeIR;

internal static class SafeFileWritePublisher
{
    public static SafeFileWritePermission EnsureAllowed(
        CapabilityGrant grant,
        string fullPath,
        long byteCount)
    {
        var allowCreate = ReadBool(grant, "allowCreate", fallback: false);
        var allowOverwrite = ReadBool(grant, "allowOverwrite", fallback: false);
        var exists = File.Exists(fullPath);
        if (exists && !allowOverwrite) {
            throw Error(SandboxErrorCode.PermissionDenied, "file.writeText denied: overwrite is not allowed");
        }

        if (!exists && !allowCreate) {
            throw Error(SandboxErrorCode.PermissionDenied, "file.writeText denied: create is not allowed");
        }

        var maxBytes = ReadLong(grant, "maxBytesPerRun", long.MaxValue);
        if (byteCount > maxBytes) {
            throw Error(SandboxErrorCode.QuotaExceeded, "file.writeText denied: content exceeds write limit");
        }

        return new SafeFileWritePermission(allowCreate, allowOverwrite);
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

        var root = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var relative = Path.GetRelativePath(root, directory);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative)) {
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

            SafeFileSystem.InvokeBeforeDirectoryCreateForTests(current);
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
        try {
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

    private static long ReadLong(CapabilityGrant grant, string key, long fallback)
    {
        if (!grant.Parameters.TryGetValue(key, out var value)) {
            return fallback;
        }

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0) {
            throw Error(SandboxErrorCode.PermissionDenied, $"file grant denied: parameter '{key}' is invalid");
        }

        return parsed;
    }

    private static bool ReadBool(CapabilityGrant grant, string key, bool fallback)
    {
        if (!grant.Parameters.TryGetValue(key, out var value)) {
            return fallback;
        }

        if (!bool.TryParse(value, out var parsed)) {
            throw Error(SandboxErrorCode.PermissionDenied, $"file grant denied: parameter '{key}' is invalid");
        }

        return parsed;
    }

    private static SandboxRuntimeException Error(SandboxErrorCode code, string message)
        => new(new SandboxError(code, message));
}

internal sealed record SafeFileWritePermission(bool AllowCreate, bool AllowOverwrite);

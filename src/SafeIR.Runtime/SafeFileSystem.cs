namespace SafeIR.Runtime;

using System.Globalization;
using System.Text;
using SafeIR;

public static class SafeFileSystem
{
    public static async ValueTask<string> ReadTextAsync(
        SandboxContext context,
        SandboxPath path,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try {
            var resolved = ResolvePath(context, path, "file.read", "file.readText");
            var info = new FileInfo(resolved.FullPath);
            if (!info.Exists) {
                throw Error(SandboxErrorCode.NotFound, "file.readText denied: file was not found");
            }

            var maxBytes = ReadLong(resolved.Grant, "maxBytesPerRun", context.Budget.Limits.MaxFileBytesRead);
            if (info.Length > maxBytes) {
                throw Error(SandboxErrorCode.QuotaExceeded, "file.readText denied: file exceeds read limit");
            }

            var bytes = await ReadLimitedBytesAsync(context, resolved, maxBytes, cancellationToken).ConfigureAwait(false);
            context.ChargeFuel(bytes.Length);
            var text = Encoding.UTF8.GetString(bytes);
            context.ChargeString(text);
            AuditRead(context, startedAt, true, resolved.SanitizedPath, bytes.Length, null);
            return text;
        }
        catch (SandboxRuntimeException ex) {
            AuditRead(context, startedAt, false, Sanitize(path.RelativePath), null, ex.Error.Code);
            throw;
        }
        catch (OperationCanceledException) {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "file.readText cancelled");
            AuditRead(context, startedAt, false, Sanitize(path.RelativePath), null, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (Exception) {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "file.readText failed");
            AuditRead(context, startedAt, false, Sanitize(path.RelativePath), null, error.Code);
            throw new SandboxRuntimeException(error);
        }
    }

    public static async ValueTask WriteTextAsync(
        SandboxContext context,
        SandboxPath path,
        string text,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try {
            var resolved = ResolvePath(context, path, "file.write", "file.writeText");
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            var permission = SafeFileWritePublisher.EnsureAllowed(resolved.Grant, resolved.FullPath, bytes.Length);
            context.Budget.ChargeFileWrite(bytes.Length);

            EnsureNoReparsePoint(resolved.RootFull, resolved.FullPath);
            SafeFileWritePublisher.EnsureParentDirectory(resolved.RootFull, resolved.FullPath, permission);
            var tempPath = resolved.FullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try {
                await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
                EnsureNoReparsePoint(resolved.RootFull, resolved.FullPath);
                SafeFileWritePublisher.PublishTempFile(tempPath, resolved.FullPath, permission);
            }
            finally {
                SafeFileWritePublisher.TryDelete(tempPath);
            }

            context.ChargeFuel(bytes.Length);
            AuditWrite(context, startedAt, true, resolved.SanitizedPath, bytes.Length, null);
        }
        catch (SandboxRuntimeException ex) {
            AuditWrite(context, startedAt, false, Sanitize(path.RelativePath), null, ex.Error.Code);
            throw;
        }
        catch (OperationCanceledException) {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "file.writeText cancelled");
            AuditWrite(context, startedAt, false, Sanitize(path.RelativePath), null, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (Exception) {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "file.writeText failed");
            AuditWrite(context, startedAt, false, Sanitize(path.RelativePath), null, error.Code);
            throw new SandboxRuntimeException(error);
        }
    }

    private static ResolvedPath ResolvePath(SandboxContext context, SandboxPath path, string capabilityId, string bindingId)
    {
        context.RequireCapability(capabilityId);
        var grant = context.GetCapability(capabilityId);
        if (!grant.Parameters.TryGetValue("root", out var root) || string.IsNullOrWhiteSpace(root)) {
            throw Error(SandboxErrorCode.PermissionDenied, $"{bindingId} denied: file root is not configured");
        }

        var relative = NormalizeRelative(path.RelativePath);
        var rootFull = EnsureTrailingSeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!IsUnderRoot(rootFull, fullPath)) {
            throw Error(SandboxErrorCode.PermissionDenied, $"{bindingId} denied: path is outside the granted sandbox root");
        }

        EnsureNoReparsePoint(rootFull, fullPath);
        EnsureExtensionAllowed(grant, fullPath);
        return new ResolvedPath(grant, rootFull, fullPath, $"sandbox://{capabilityId}/" + relative.Replace('\\', '/'));
    }

    private static string NormalizeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            Uri.TryCreate(path, UriKind.Absolute, out _) ||
            Path.IsPathFullyQualified(path) ||
            path.StartsWith('\\') ||
            path.StartsWith('/')) {
            throw Error(SandboxErrorCode.PermissionDenied, "file path denied: absolute paths are not allowed");
        }

        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static async ValueTask<byte[]> ReadLimitedBytesAsync(
        SandboxContext context,
        ResolvedPath resolved,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            resolved.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        EnsureNoReparsePoint(resolved.RootFull, resolved.FullPath);
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        while (true) {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) {
                return memory.ToArray();
            }

            if (memory.Length + read > maxBytes) {
                throw Error(SandboxErrorCode.QuotaExceeded, "file.readText denied: file exceeds read limit");
            }

            context.Budget.ChargeFileRead(read);
            memory.Write(buffer, 0, read);
        }
    }

    internal static void EnsureNoReparsePoint(string rootFull, string fullPath)
    {
        var root = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        CheckAttributes(root);

        var relative = Path.GetRelativePath(root, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative)) {
            throw Error(SandboxErrorCode.PermissionDenied, "file access denied: path is outside the granted sandbox root");
        }

        var current = root;
        foreach (var part in relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries)) {
            current = Path.Combine(current, part);
            if (Directory.Exists(current) || File.Exists(current)) {
                CheckAttributes(current);
            }
        }
    }

    private static void CheckAttributes(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0) {
            throw Error(SandboxErrorCode.PermissionDenied, "file access denied: reparse points are not allowed");
        }
    }

    private static void EnsureExtensionAllowed(CapabilityGrant grant, string fullPath)
    {
        if (!grant.Parameters.TryGetValue("allowedExtensions", out var allowed) || string.IsNullOrWhiteSpace(allowed)) {
            return;
        }

        var extension = Path.GetExtension(fullPath);
        var values = allowed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!values.Any(v => StringComparer.OrdinalIgnoreCase.Equals(v, extension))) {
            throw Error(SandboxErrorCode.PermissionDenied, "file.readText denied: extension is not allowed");
        }
    }

    private static void AuditRead(
        SandboxContext context,
        DateTimeOffset startedAt,
        bool success,
        string resource,
        long? bytes,
        SandboxErrorCode? error)
        => Audit(context, startedAt, success, "file.readText", "file.read", SandboxEffect.FileRead, resource, bytes, error);

    private static void AuditWrite(
        SandboxContext context,
        DateTimeOffset startedAt,
        bool success,
        string resource,
        long? bytes,
        SandboxErrorCode? error)
        => Audit(context, startedAt, success, "file.writeText", "file.write", SandboxEffect.FileWrite, resource, bytes, error);

    private static void Audit(
        SandboxContext context,
        DateTimeOffset startedAt,
        bool success,
        string bindingId,
        string capabilityId,
        SandboxEffect effect,
        string resource,
        long? bytes,
        SandboxErrorCode? error)
        => context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "BindingCall",
            startedAt,
            success,
            BindingId: bindingId,
            CapabilityId: capabilityId,
            Effect: effect,
            ResourceId: resource,
            ErrorCode: error,
            Bytes: bytes));

    private static bool IsUnderRoot(string rootFull, string fullPath)
        => fullPath.StartsWith(rootFull, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string EnsureTrailingSeparator(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

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

    private static string Sanitize(string value) => value.Replace('\\', '/');

    private static SandboxRuntimeException Error(SandboxErrorCode code, string message) => new(new SandboxError(code, message));

    private sealed record ResolvedPath(CapabilityGrant Grant, string RootFull, string FullPath, string SanitizedPath);
}

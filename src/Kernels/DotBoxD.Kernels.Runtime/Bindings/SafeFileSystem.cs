using System.Buffers;
using System.Text;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime.Bindings;

public static partial class SafeFileSystem
{
    private static readonly char[] PathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
    public static async ValueTask<string> ReadTextAsync(
        SandboxContext context,
        SandboxPath path,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var fileBytesReadBefore = context.Budget.FileBytesRead;
        try
        {
            var resolved = ResolvePath(context, path, "file.read", "file.readText");
            var info = new FileInfo(resolved.FullPath);
            if (!info.Exists)
            {
                throw Error(SandboxErrorCode.NotFound, "file.readText denied: file was not found");
            }

            var maxBytes = SafeFileGrantReader.Read(resolved.Grant).MaxBytesPerRun
                ?? context.Budget.Limits.MaxFileBytesRead;
            if (info.Length > maxBytes)
            {
                throw Error(SandboxErrorCode.QuotaExceeded, "file.readText denied: file exceeds read limit");
            }

            using var bytes = await ReadLimitedBytesAsync(context, resolved, maxBytes, cancellationToken).ConfigureAwait(false);
            var length = CheckedLength(bytes.Length);
            var buffer = bytes.GetBuffer();
            context.ChargeFuel(length);
            context.ChargeStringAllocation(Encoding.UTF8.GetCharCount(buffer, 0, length));
            var text = Encoding.UTF8.GetString(buffer, 0, length);
            context.RecordStringReturnCredit(text);
            SafeFileAudit.Read(context, startedAt, true, resolved.SanitizedPath, length, null);
            return text;
        }
        catch (SandboxRuntimeException ex)
        {
            SafeFileAudit.Read(context, startedAt, false, FailureResource(path, "file.read"), ObservedReadBytes(context, fileBytesReadBefore), ex.Error.Code);
            throw;
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            var error = new SandboxError(SandboxErrorCode.Timeout, "file.readText denied: request timed out");
            SafeFileAudit.Read(context, startedAt, false, FailureResource(path, "file.read"), ObservedReadBytes(context, fileBytesReadBefore), error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "file.readText cancelled");
            SafeFileAudit.Read(context, startedAt, false, FailureResource(path, "file.read"), ObservedReadBytes(context, fileBytesReadBefore), error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "file.readText failed");
            SafeFileAudit.Read(context, startedAt, false, FailureResource(path, "file.read"), ObservedReadBytes(context, fileBytesReadBefore), error.Code);
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
        try
        {
            var resolved = ResolvePath(context, path, "file.write", "file.writeText");
            var byteCount = Encoding.UTF8.GetByteCount(text);
            var permission = SafeFileWritePublisher.EnsureAllowed(resolved.Grant, resolved.FullPath, byteCount);
            context.Budget.ChargeFileWrite(byteCount);
            context.ChargeAllocation(byteCount);
            context.ChargeFuel(byteCount);
            var bytes = Encoding.UTF8.GetBytes(text);

            context.Budget.CheckDeadline();
            EnsureNoReparsePoint(resolved.RootFull, resolved.FullPath);
            FileSystem.SafeFileSystem.EnsureDirectWritePath(resolved.RootFull, resolved.FullPath);
            SafeFileWritePublisher.EnsureParentDirectory(resolved.RootFull, resolved.FullPath, permission);
            var tempPath = resolved.FullPath + ".tmp-" + FileSystem.SafeFileSystem.CreateTempSuffix();
            try
            {
                FileSystem.SafeFileSystem.BeforeTempCreateForTests.Value?.Invoke();
                EnsureNoReparsePoint(resolved.RootFull, tempPath);
                var temp = SafeFileNoFollow.CreateNewWrite(tempPath);
                await using (temp.ConfigureAwait(false))
                {
                    await temp.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await temp.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                context.CancellationToken.ThrowIfCancellationRequested();
                context.Budget.CheckDeadline();
                EnsureNoReparsePoint(resolved.RootFull, tempPath);
                EnsureNoReparsePoint(resolved.RootFull, resolved.FullPath);
                SafeFileWritePublisher.PublishTempFile(tempPath, resolved.FullPath, permission);
                context.Budget.CheckDeadline();
            }
            finally
            {
                SafeFileWritePublisher.TryDelete(tempPath);
            }

            SafeFileAudit.Write(context, startedAt, true, resolved.SanitizedPath, byteCount, null);
        }
        catch (SandboxRuntimeException ex)
        {
            SafeFileAudit.Write(context, startedAt, false, FailureResource(path, "file.write"), null, ex.Error.Code);
            throw;
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            var error = new SandboxError(SandboxErrorCode.Timeout, "file.writeText denied: request timed out");
            SafeFileAudit.Write(context, startedAt, false, FailureResource(path, "file.write"), null, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "file.writeText cancelled");
            SafeFileAudit.Write(context, startedAt, false, FailureResource(path, "file.write"), null, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "file.writeText failed");
            SafeFileAudit.Write(context, startedAt, false, FailureResource(path, "file.write"), null, error.Code);
            throw new SandboxRuntimeException(error);
        }
    }

    private static ResolvedPath ResolvePath(SandboxContext context, SandboxPath path, string capabilityId, string bindingId)
    {
        // GetCapability performs the authorization (and denial audit) in one indexed
        // lookup, so a preceding RequireCapability would only repeat the same scan.
        var grant = context.GetCapability(capabilityId);
        if (!grant.Parameters.TryGetValue("root", out var root) || string.IsNullOrWhiteSpace(root))
        {
            throw Error(SandboxErrorCode.PermissionDenied, $"{bindingId} denied: file root is not configured");
        }

        var relative = NormalizeRelative(path.RelativePath);
        var rootFull = EnsureTrailingSeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!IsUnderRoot(rootFull, fullPath))
        {
            throw Error(SandboxErrorCode.PermissionDenied, $"{bindingId} denied: path is outside the granted sandbox root");
        }

        EnsureNoReparsePoint(rootFull, fullPath);
        EnsureExtensionAllowed(grant, fullPath);
        return new ResolvedPath(grant, rootFull, fullPath, $"sandbox://{capabilityId}/" + relative.Replace('\\', '/'));
    }

    private static string NormalizeRelative(string path)
    {
        if (!SandboxLiteralConstraints.IsPortableRelativePath(path))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "file path denied: path is not a portable relative path");
        }

        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static string FailureResource(SandboxPath path, string capabilityId)
        => SandboxLiteralConstraints.IsPortableRelativePath(path.RelativePath)
            ? path.RelativePath
            : $"sandbox://{capabilityId}/[invalid-path]";

    private static long? ObservedReadBytes(SandboxContext context, long fileBytesReadBefore)
    {
        var observedBytes = context.Budget.FileBytesRead - fileBytesReadBefore;
        return observedBytes > 0 ? observedBytes : null;
    }

    private static async ValueTask<MemoryStream> ReadLimitedBytesAsync(
        SandboxContext context,
        ResolvedPath resolved,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var stream = SafeFileNoFollow.OpenRead(resolved.FullPath);
        await using (stream.ConfigureAwait(false))
        {
            EnsureNoReparsePoint(resolved.RootFull, resolved.FullPath);
            var memory = new MemoryStream();
            var buffer = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                while (true)
                {
                    context.Budget.CheckDeadline();
                    var read = await SafeFileNoFollow.ReadAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
                    context.Budget.CheckDeadline();
                    if (read == 0)
                    {
                        return memory;
                    }

                    context.Budget.ChargeFileRead(read);
                    if (memory.Length + read > maxBytes)
                    {
                        throw Error(SandboxErrorCode.QuotaExceeded, "file.readText denied: file exceeds read limit");
                    }

                    context.ChargeAllocation(read);
                    memory.Write(buffer, 0, read);
                }
            }
            catch
            {
                memory.Dispose();
                throw;
            }
            finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
        }
    }

    private static int CheckedLength(long length)
    {
        if (length > int.MaxValue)
        {
            throw Error(SandboxErrorCode.QuotaExceeded, "file.readText denied: file exceeds read limit");
        }

        return (int)length;
    }

    internal static void EnsureNoReparsePoint(string rootFull, string fullPath)
    {
        var root = Path.TrimEndingDirectorySeparator(rootFull);
        CheckAttributes(root);

        var relative = Path.GetRelativePath(root, fullPath);
        if (IsRootEscapeRelativePath(relative))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "file access denied: path is outside the granted sandbox root");
        }

        var current = root;
        foreach (var part in relative.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (SafeFilePathAttributes.IsReparsePointIfAccessible(current))
            {
                throw Error(SandboxErrorCode.PermissionDenied, "file access denied: reparse points are not allowed");
            }
        }
    }

    internal static bool IsRootEscapeRelativePath(string relative)
        => Path.IsPathFullyQualified(relative) ||
           relative.Equals("..", StringComparison.Ordinal) ||
           relative.StartsWith("../", StringComparison.Ordinal) ||
           relative.StartsWith(@"..\", StringComparison.Ordinal);

    private static void CheckAttributes(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw Error(SandboxErrorCode.PermissionDenied, "file access denied: reparse points are not allowed");
        }
    }

    private static void EnsureExtensionAllowed(CapabilityGrant grant, string fullPath)
    {
        var allowed = SafeFileGrantReader.Read(grant).AllowedExtensions;
        if (allowed is null)
        {
            return;
        }

        var extension = Path.GetExtension(fullPath);
        if (!allowed.Contains(extension))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "file.readText denied: extension is not allowed");
        }
    }

    private static bool IsUnderRoot(string rootFull, string fullPath)
        => fullPath.StartsWith(rootFull, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string EnsureTrailingSeparator(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    internal static SandboxRuntimeException Error(SandboxErrorCode code, string message) => new(new SandboxError(code, message));

    private sealed record ResolvedPath(CapabilityGrant Grant, string RootFull, string FullPath, string SanitizedPath);
}

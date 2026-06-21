using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime;

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal static partial class SafeFileNoFollow
{
    private const int BufferSize = 4096;

    public static FileStream OpenRead(string fullPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return OpenWindows(fullPath);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return OpenUnix(fullPath);
        }

        return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.None, BufferSize, useAsync: true);
    }

    public static FileStream CreateNewWrite(string fullPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateNewWriteWindows(fullPath);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return CreateNewWriteUnix(fullPath);
        }

        return new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
    }

    public static ValueTask<int> ReadAsync(
        FileStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        if (stream.IsAsync)
        {
            return stream.ReadAsync(buffer, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var read = stream.Read(buffer, 0, buffer.Length);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(read);
    }

    private static FileStream OpenWindows(string fullPath)
    {
        var handle = CreateFileW(
            fullPath,
            WindowsGenericRead,
            WindowsFileShareRead,
            IntPtr.Zero,
            WindowsOpenExisting,
            WindowsOpenFlags,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw WindowsOpenError(Marshal.GetLastWin32Error());
        }

        try
        {
            if (!GetFileInformationByHandle(handle, out var info))
            {
                throw WindowsOpenError(Marshal.GetLastWin32Error());
            }

            if ((info.FileAttributes & WindowsFileAttributeReparsePoint) != 0)
            {
                throw Denied();
            }

            return new FileStream(handle, FileAccess.Read, BufferSize, isAsync: true);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static FileStream CreateNewWriteWindows(string fullPath)
    {
        var handle = CreateFileW(
            fullPath,
            WindowsGenericWrite,
            WindowsFileShareNone,
            IntPtr.Zero,
            WindowsCreateNew,
            WindowsOpenFlags,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw WindowsCreateError(Marshal.GetLastWin32Error());
        }

        try
        {
            if (!GetFileInformationByHandle(handle, out var info))
            {
                throw WindowsCreateError(Marshal.GetLastWin32Error());
            }

            if ((info.FileAttributes & WindowsFileAttributeReparsePoint) != 0)
            {
                throw Denied();
            }

            return new FileStream(handle, FileAccess.Write, BufferSize, isAsync: true);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static FileStream OpenUnix(string fullPath)
    {
        var fd = open(fullPath, UnixOpenFlags(), mode: 0);
        if (fd == -1)
        {
            throw UnixOpenError(Marshal.GetLastWin32Error());
        }

        var handle = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        // POSIX open returns a synchronous descriptor; .NET rejects marking that handle async.
        return new FileStream(handle, FileAccess.Read, BufferSize, isAsync: false);
    }

    private static FileStream CreateNewWriteUnix(string fullPath)
    {
        var fd = open(fullPath, UnixCreateFlags(), mode: UnixOwnerReadWriteMode);
        if (fd == -1)
        {
            throw UnixCreateError(Marshal.GetLastWin32Error());
        }

        try
        {
            if (fchmod(fd, UnixOwnerReadWriteMode) == -1)
            {
                throw UnixCreateError(Marshal.GetLastWin32Error());
            }

            var handle = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
            fd = -1;
            return new FileStream(handle, FileAccess.Write, BufferSize, isAsync: false);
        }
        finally
        {
            if (fd != -1)
            {
                close(fd);
            }
        }
    }

    private static int UnixOpenFlags()
        => OperatingSystem.IsMacOS()
            ? MacOpenNoFollow | MacOpenCloseOnExec
            : LinuxOpenNoFollow | LinuxOpenCloseOnExec;

    private static int UnixCreateFlags()
        => OperatingSystem.IsMacOS()
            ? UnixOpenWriteOnly | MacOpenCreate | MacOpenExclusive | MacOpenNoFollow | MacOpenCloseOnExec
            : UnixOpenWriteOnly | LinuxOpenCreate | LinuxOpenExclusive | LinuxOpenNoFollow | LinuxOpenCloseOnExec;

    private static Exception WindowsOpenError(int error)
        => error switch
        {
            WindowsErrorFileNotFound or WindowsErrorPathNotFound => NotFound(),
            WindowsErrorAccessDenied => Denied(),
            _ => new IOException("file.readText denied: file could not be opened")
        };

    private static Exception WindowsCreateError(int error)
        => error switch
        {
            WindowsErrorFileNotFound or WindowsErrorPathNotFound => NotFound(),
            WindowsErrorAccessDenied or WindowsErrorFileExists or WindowsErrorAlreadyExists => Denied(),
            _ => new IOException("file.writeText denied: temp file could not be created")
        };

    private static Exception UnixOpenError(int error)
        => error switch
        {
            UnixErrorNoEntry or UnixErrorNotDirectory => NotFound(),
            UnixErrorAccessDenied => Denied(),
            _ when error == UnixLoopError() => Denied(),
            _ => new IOException("file.readText denied: file could not be opened")
        };

    private static Exception UnixCreateError(int error)
        => error switch
        {
            UnixErrorNoEntry or UnixErrorNotDirectory => NotFound(),
            UnixErrorAccessDenied or UnixErrorExists => Denied(),
            _ when error == UnixLoopError() => Denied(),
            _ => new IOException("file.writeText denied: temp file could not be created")
        };

    private static int UnixLoopError() => OperatingSystem.IsMacOS() ? MacErrorLoop : LinuxErrorLoop;

    private static SandboxRuntimeException NotFound()
        => new(new SandboxError(SandboxErrorCode.NotFound, "file.readText denied: file was not found"));

    private static SandboxRuntimeException Denied()
        => new(new SandboxError(SandboxErrorCode.PermissionDenied, "file access denied: reparse points are not allowed"));
}

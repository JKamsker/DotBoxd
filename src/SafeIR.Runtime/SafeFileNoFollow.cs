namespace SafeIR.Runtime;

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SafeIR;

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

    private static FileStream OpenUnix(string fullPath)
    {
        var fd = open(fullPath, UnixOpenFlags(), mode: 0);
        if (fd == -1)
        {
            throw UnixOpenError(Marshal.GetLastWin32Error());
        }

        var handle = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        return new FileStream(handle, FileAccess.Read, BufferSize, isAsync: true);
    }

    private static int UnixOpenFlags()
        => OperatingSystem.IsMacOS()
            ? MacOpenNoFollow | MacOpenCloseOnExec
            : LinuxOpenNoFollow | LinuxOpenCloseOnExec;

    private static Exception WindowsOpenError(int error)
        => error switch
        {
            WindowsErrorFileNotFound or WindowsErrorPathNotFound => NotFound(),
            WindowsErrorAccessDenied => Denied(),
            _ => new IOException("file.readText denied: file could not be opened")
        };

    private static Exception UnixOpenError(int error)
        => error switch
        {
            UnixErrorNoEntry or UnixErrorNotDirectory => NotFound(),
            UnixErrorAccessDenied => Denied(),
            _ when error == UnixLoopError() => Denied(),
            _ => new IOException("file.readText denied: file could not be opened")
        };

    private static int UnixLoopError() => OperatingSystem.IsMacOS() ? MacErrorLoop : LinuxErrorLoop;

    private static SandboxRuntimeException NotFound()
        => new(new SandboxError(SandboxErrorCode.NotFound, "file.readText denied: file was not found"));

    private static SandboxRuntimeException Denied()
        => new(new SandboxError(SandboxErrorCode.PermissionDenied, "file access denied: reparse points are not allowed"));
}

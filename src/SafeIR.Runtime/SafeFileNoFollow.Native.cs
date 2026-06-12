namespace SafeIR.Runtime;

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal static partial class SafeFileNoFollow
{
    private const uint WindowsGenericRead = 0x80000000;
    private const uint WindowsFileShareRead = 0x00000001;
    private const uint WindowsOpenExisting = 3;
    private const uint WindowsFileAttributeReparsePoint = 0x00000400;
    private const uint WindowsFileAttributeNormal = 0x00000080;
    private const uint WindowsFileFlagOverlapped = 0x40000000;
    private const uint WindowsFileFlagOpenReparsePoint = 0x00200000;
    private const uint WindowsFileFlagSequentialScan = 0x08000000;
    private const uint WindowsOpenFlags =
        WindowsFileAttributeNormal |
        WindowsFileFlagOverlapped |
        WindowsFileFlagOpenReparsePoint |
        WindowsFileFlagSequentialScan;
    private const int WindowsErrorFileNotFound = 2;
    private const int WindowsErrorPathNotFound = 3;
    private const int WindowsErrorAccessDenied = 5;

    private const int LinuxOpenNoFollow = 0x20000;
    private const int LinuxOpenCloseOnExec = 0x80000;
    private const int LinuxErrorLoop = 40;
    private const int MacOpenNoFollow = 0x100;
    private const int MacOpenCloseOnExec = 0x1000000;
    private const int MacErrorLoop = 62;
    private const int UnixErrorNoEntry = 2;
    private const int UnixErrorAccessDenied = 13;
    private const int UnixErrorNotDirectory = 20;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation fileInformation);

    [DllImport("libc", SetLastError = true, EntryPoint = "open")]
    private static extern int open(string pathname, int flags, uint mode);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}

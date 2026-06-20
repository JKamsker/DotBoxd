using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Internal;

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

internal static class PersistentCompiledArtifactCacheRootGuard
{
    public static void Validate(string rootDirectory)
    {
        var info = new DirectoryInfo(rootDirectory);
        if (!info.Exists)
        {
            throw Denied("compiled cache directory does not exist");
        }

        info.Refresh();
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw Denied("compiled cache directory must not be a reparse point");
        }

        ValidatePlatformPermissions(info);
        ProbeExclusiveWrite(info.FullName);
    }

    private static void ValidatePlatformPermissions(DirectoryInfo info)
    {
        if (OperatingSystem.IsWindows())
        {
            ValidateWindowsAcl(info);
            return;
        }

        ValidateUnixMode(info);
    }

    [UnsupportedOSPlatform("windows")]
    private static void ValidateUnixMode(DirectoryInfo info)
    {
        var mode = File.GetUnixFileMode(info.FullName);
        const UnixFileMode unsafeWrite =
            UnixFileMode.GroupWrite |
            UnixFileMode.OtherWrite;
        if ((mode & unsafeWrite) != 0)
        {
            throw Denied("compiled cache directory must not be group- or world-writable");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ValidateWindowsAcl(DirectoryInfo info)
    {
        var security = info.GetAccessControl(AccessControlSections.Access);
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow ||
                !GrantsWrite(rule.FileSystemRights) ||
                rule.IdentityReference is not SecurityIdentifier sid ||
                !IsBroadPrincipal(sid))
            {
                continue;
            }

            throw Denied("compiled cache directory must not grant write access to broad Windows principals");
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool GrantsWrite(FileSystemRights rights)
    {
        const FileSystemRights writeRights =
            FileSystemRights.Write |
            FileSystemRights.WriteData |
            FileSystemRights.CreateFiles |
            FileSystemRights.CreateDirectories |
            FileSystemRights.AppendData |
            FileSystemRights.WriteAttributes |
            FileSystemRights.WriteExtendedAttributes |
            FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership |
            FileSystemRights.Modify |
            FileSystemRights.FullControl;
        return (rights & writeRights) != 0;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsBroadPrincipal(SecurityIdentifier sid)
        => sid.IsWellKnown(WellKnownSidType.WorldSid) ||
           sid.IsWellKnown(WellKnownSidType.AuthenticatedUserSid) ||
           sid.IsWellKnown(WellKnownSidType.BuiltinUsersSid) ||
           sid.IsWellKnown(WellKnownSidType.BuiltinGuestsSid) ||
           sid.IsWellKnown(WellKnownSidType.AnonymousSid) ||
           sid.IsWellKnown(WellKnownSidType.InteractiveSid);

    private static void ProbeExclusiveWrite(string rootDirectory)
    {
        var path = Path.Combine(rootDirectory, ".dotboxd-cache-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough))
            {
            }
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static SandboxRuntimeException Denied(string message)
        => new(new SandboxError(SandboxErrorCode.PermissionDenied, message));
}

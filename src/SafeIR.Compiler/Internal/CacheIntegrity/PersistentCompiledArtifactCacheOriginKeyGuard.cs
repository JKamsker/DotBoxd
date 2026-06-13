namespace SafeIR.Compiler.Internal;

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using SafeIR;

/// <summary>
/// Hardens and validates the on-disk HMAC origin-key directory and file so the
/// signing trust root is at least as protected as the cache root guarded by
/// <see cref="PersistentCompiledArtifactCacheRootGuard"/>. The origin key is a host
/// secret: if it can be read or replaced by another local user or process through
/// inherited permissions, that actor can forge valid origin proofs. Creation is
/// owner-only and reads fail closed when broad access is detected or cannot be
/// verified.
/// </summary>
internal static class PersistentCompiledArtifactCacheOriginKeyGuard
{
    /// <summary>
    /// Restricts the directory to owner-only access. On Unix this sets mode 0700;
    /// on Windows it replaces the DACL with a single owner allow-rule and disables
    /// inheritance so broad principals cannot read or replace the signing key.
    /// </summary>
    public static void HardenDirectory(string directoryPath)
    {
        if (OperatingSystem.IsWindows())
        {
            HardenWindowsDirectory(new DirectoryInfo(directoryPath));
            return;
        }

        File.SetUnixFileMode(
            directoryPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>
    /// Restricts the key file to owner-only read/write access (Unix mode 0600;
    /// Windows owner-only DACL with inheritance disabled).
    /// </summary>
    public static void HardenFile(string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            HardenWindowsFile(new FileInfo(filePath));
            return;
        }

        File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>
    /// Fails closed if the directory grants group/world or broad-principal access,
    /// or if its permissions cannot be read.
    /// </summary>
    public static void ValidateDirectory(string directoryPath)
    {
        if (OperatingSystem.IsWindows())
        {
            ValidateWindowsAcl(GetWindowsDirectoryAccess(new DirectoryInfo(directoryPath)));
            return;
        }

        ValidateUnixMode(directoryPath);
    }

    /// <summary>
    /// Fails closed if the key file grants group/world or broad-principal access,
    /// or if its permissions cannot be read.
    /// </summary>
    public static void ValidateFile(string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            ValidateWindowsAcl(GetWindowsFileAccess(new FileInfo(filePath)));
            return;
        }

        ValidateUnixMode(filePath);
    }

    [UnsupportedOSPlatform("windows")]
    private static void ValidateUnixMode(string path)
    {
        UnixFileMode mode;
        try
        {
            mode = File.GetUnixFileMode(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Denied("origin key permissions could not be verified");
        }

        const UnixFileMode broadAccess =
            UnixFileMode.GroupRead |
            UnixFileMode.GroupWrite |
            UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead |
            UnixFileMode.OtherWrite |
            UnixFileMode.OtherExecute;
        if ((mode & broadAccess) != 0)
        {
            throw Denied("origin key must not be group- or world-accessible");
        }
    }

    [SupportedOSPlatform("windows")]
    private static AuthorizationRuleCollection GetWindowsDirectoryAccess(DirectoryInfo info)
        => GetWindowsAccessRules(() => info.GetAccessControl(AccessControlSections.Access));

    [SupportedOSPlatform("windows")]
    private static AuthorizationRuleCollection GetWindowsFileAccess(FileInfo info)
        => GetWindowsAccessRules(() => info.GetAccessControl(AccessControlSections.Access));

    [SupportedOSPlatform("windows")]
    private static AuthorizationRuleCollection GetWindowsAccessRules(Func<FileSystemSecurity> read)
    {
        try
        {
            return read().GetAccessRules(true, true, typeof(SecurityIdentifier));
        }
        catch (SystemException)
        {
            // IOException, UnauthorizedAccessException, PlatformNotSupportedException,
            // and identity/privilege failures all derive from SystemException. Any
            // failure to read the DACL means we cannot prove the key is private, so
            // fail closed.
            throw Denied("origin key permissions could not be verified");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ValidateWindowsAcl(AuthorizationRuleCollection rules)
    {
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow ||
                !GrantsReadOrWrite(rule.FileSystemRights) ||
                rule.IdentityReference is not SecurityIdentifier sid ||
                !IsBroadPrincipal(sid))
            {
                continue;
            }

            throw Denied("origin key must not grant access to broad Windows principals");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void HardenWindowsDirectory(DirectoryInfo info)
    {
        try
        {
            var security = info.GetAccessControl(AccessControlSections.Access);
            RestrictToOwner(
                security,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
            info.SetAccessControl(security);
        }
        catch (SystemException)
        {
            throw Denied("origin key permissions could not be hardened");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void HardenWindowsFile(FileInfo info)
    {
        try
        {
            var security = info.GetAccessControl(AccessControlSections.Access);
            RestrictToOwner(security, InheritanceFlags.None);
            info.SetAccessControl(security);
        }
        catch (SystemException)
        {
            throw Denied("origin key permissions could not be hardened");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RestrictToOwner(
        FileSystemSecurity security,
        InheritanceFlags inheritanceFlags)
    {
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            security.RemoveAccessRule(rule);
        }

        var owner = WindowsIdentity.GetCurrent().User
            ?? throw Denied("origin key owner identity is unavailable");
        security.AddAccessRule(new FileSystemAccessRule(
            owner,
            FileSystemRights.FullControl,
            inheritanceFlags,
            PropagationFlags.None,
            AccessControlType.Allow));
    }

    [SupportedOSPlatform("windows")]
    private static bool GrantsReadOrWrite(FileSystemRights rights)
    {
        const FileSystemRights sensitiveRights =
            FileSystemRights.Read |
            FileSystemRights.ReadData |
            FileSystemRights.ReadAttributes |
            FileSystemRights.ReadExtendedAttributes |
            FileSystemRights.ReadPermissions |
            FileSystemRights.ListDirectory |
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
        return (rights & sensitiveRights) != 0;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsBroadPrincipal(SecurityIdentifier sid)
        => sid.IsWellKnown(WellKnownSidType.WorldSid) ||
           sid.IsWellKnown(WellKnownSidType.AuthenticatedUserSid) ||
           sid.IsWellKnown(WellKnownSidType.BuiltinUsersSid) ||
           sid.IsWellKnown(WellKnownSidType.BuiltinGuestsSid) ||
           sid.IsWellKnown(WellKnownSidType.AnonymousSid) ||
           sid.IsWellKnown(WellKnownSidType.InteractiveSid);

    private static SandboxRuntimeException Denied(string message)
        => new(new SandboxError(SandboxErrorCode.PermissionDenied, message));
}

using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime.Bindings;

internal static class SafeFilePathAttributes
{
    public static bool IsReparsePointOrDeny(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            throw SafeFileSystem.Error(
                SandboxErrorCode.PermissionDenied,
                "file access denied: path attributes could not be read");
        }
        catch (IOException)
        {
            throw SafeFileSystem.Error(
                SandboxErrorCode.PermissionDenied,
                "file access denied: path attributes could not be verified");
        }
    }
}

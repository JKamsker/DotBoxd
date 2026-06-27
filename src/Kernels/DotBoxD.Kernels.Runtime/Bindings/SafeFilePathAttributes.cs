namespace DotBoxD.Kernels.Runtime.Bindings;

internal static class SafeFilePathAttributes
{
    public static bool IsReparsePointIfAccessible(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

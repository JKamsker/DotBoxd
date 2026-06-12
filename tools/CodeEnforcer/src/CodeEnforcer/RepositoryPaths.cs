namespace CodeEnforcer;

internal static class RepositoryPaths
{
    public static string DiscoverRoot(string startDirectory)
    {
        DirectoryInfo? current = new(Path.GetFullPath(startDirectory));
        while (current is not null)
        {
            string gitPath = Path.Combine(current.FullName, ".git");
            if (File.Exists(gitPath) || Directory.Exists(gitPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new CodeEnforcerException("Could not find repository root.", ExitCodes.InputError);
    }
}

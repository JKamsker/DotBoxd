namespace SafeIR.Compiler.Internal;

internal static class PersistentCompiledArtifactCachePublisher
{
    public static bool MoveExistingEntryAside(string finalPath, string previousPath)
    {
        if (Directory.Exists(finalPath)) {
            Directory.Move(finalPath, previousPath);
            return true;
        }

        if (File.Exists(finalPath)) {
            File.Move(finalPath, previousPath);
            return true;
        }

        return false;
    }

    public static void RestorePreviousEntry(string finalPath, string previousPath, bool movedPrevious)
    {
        if (!movedPrevious || Directory.Exists(finalPath) || File.Exists(finalPath)) {
            return;
        }

        if (Directory.Exists(previousPath)) {
            Directory.Move(previousPath, finalPath);
        }
        else if (File.Exists(previousPath)) {
            File.Move(previousPath, finalPath);
        }
    }

    public static void DeleteEntryIfExists(string path)
    {
        if (Directory.Exists(path)) {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path)) {
            File.Delete(path);
        }
    }

    public static void ValidateEntryShape(string entryPath)
    {
        var required = new HashSet<string>(StringComparer.Ordinal) {
            "module.dll", "manifest.json", "verification.json", PersistentCompiledArtifactCacheOrigin.ProofFileName
        };
        foreach (var file in Directory.EnumerateFiles(entryPath)) {
            if (!required.Remove(Path.GetFileName(file))) {
                throw new IOException("compiled cache entry contains unexpected file");
            }
        }

        if (required.Count > 0 || Directory.EnumerateDirectories(entryPath).Any()) {
            throw new IOException("compiled cache entry is incomplete");
        }
    }
}

namespace CodeEnforcer;

internal sealed record CodeFile(string Path, int LineCount)
{
    public string Folder => PathUtility.GetDirectory(Path);
}

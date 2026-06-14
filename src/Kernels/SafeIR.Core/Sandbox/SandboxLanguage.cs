namespace SafeIR;

public static class SandboxLanguage
{
    public static SemVersion CurrentVersion { get; } = SemVersion.One;

    public static string CurrentVersionText => CurrentVersion.ToString();

    public static bool Supports(SemVersion target)
        => target.Major == CurrentVersion.Major && target.CompareTo(CurrentVersion) <= 0;
}

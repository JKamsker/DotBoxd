using System.Globalization;

namespace SafeIR;

public sealed record SemVersion(int Major, int Minor, int Patch)
    : IComparable<SemVersion>
{
    public static SemVersion One { get; } = new(1, 0, 0);

    public static SemVersion Parse(string text)
    {
        if (!TryParse(text, out var version)) {
            throw new FormatException($"Invalid semantic version '{text}'.");
        }

        return version;
    }

    public static bool TryParse(string text, out SemVersion version)
    {
        version = One;
        var value = text.StartsWith('v') ? text[1..] : text;
        var parts = value.Split('.');

        if (parts.Length == 1 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)) {
            version = new SemVersion(major, 0, 0);
            return true;
        }

        if (parts.Length != 3) {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch)) {
            return false;
        }

        version = new SemVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null) {
            return 1;
        }

        var major = Major.CompareTo(other.Major);
        if (major != 0) {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

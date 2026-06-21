using System.Globalization;

namespace DotBoxD.Kernels.Model;

public sealed record SemVersion(int Major, int Minor, int Patch)
    : IComparable<SemVersion>
{
    private int _major = RequireNonNegative(Major, nameof(Major));
    private int _minor = RequireNonNegative(Minor, nameof(Minor));
    private int _patch = RequireNonNegative(Patch, nameof(Patch));

    public static SemVersion One { get; } = new(1, 0, 0);

    public int Major { get => _major; init => _major = RequireNonNegative(value, nameof(Major)); }
    public int Minor { get => _minor; init => _minor = RequireNonNegative(value, nameof(Minor)); }
    public int Patch { get => _patch; init => _patch = RequireNonNegative(value, nameof(Patch)); }

    public static SemVersion Parse(string text)
    {
        if (!TryParse(text, out var version))
        {
            throw new FormatException($"Invalid semantic version '{text}'.");
        }

        return version;
    }

    public static bool TryParse(string text, out SemVersion version)
    {
        version = One;
        var value = text.StartsWith('v') ? text[1..] : text;
        var parts = value.Split('.');

        if (parts.Length == 1 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major))
        {
            version = new SemVersion(major, 0, 0);
            return true;
        }

        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        version = new SemVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    private static int RequireNonNegative(int value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "semantic version components must be non-negative.");
        }

        return value;
    }
}

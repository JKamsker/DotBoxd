using System.Text;

namespace SafeIR;

public sealed record SandboxType(string Name, IReadOnlyList<SandboxType> Arguments)
{
    private static readonly HashSet<string> AllowedScalars = new(StringComparer.Ordinal) {
        "Unit", "Bool", "I32", "I64", "F64", "String",
        "SandboxPath", "SandboxUri", "PlayerId", "ItemId", "QuestId", "MapId"
    };

    private static readonly HashSet<string> MapKeyScalars = new(StringComparer.Ordinal) {
        "Bool", "I32", "I64", "String", "SandboxPath", "SandboxUri",
        "PlayerId", "ItemId", "QuestId", "MapId"
    };

    private static readonly HashSet<string> ForbiddenNames = new(StringComparer.OrdinalIgnoreCase) {
        "Object", "Dynamic", "Type", "Assembly", "MemberInfo", "MethodInfo", "PropertyInfo",
        "FieldInfo", "ConstructorInfo", "Module", "RuntimeTypeHandle", "RuntimeMethodHandle",
        "RuntimeFieldHandle", "Delegate", "Expression", "IQueryable", "IServiceProvider",
        "ServiceProvider", "Stream", "TextReader", "TextWriter", "FileInfo", "DirectoryInfo",
        "DriveInfo", "HttpClient", "Socket", "DbConnection", "DbContext", "Process",
        "Thread", "Task", "CancellationTokenSource", "IntPtr", "UIntPtr", "SafeHandle",
        "Span", "Memory", "Pointer"
    };

    public static SandboxType Unit { get; } = Scalar("Unit");
    public static SandboxType Bool { get; } = Scalar("Bool");
    public static SandboxType I32 { get; } = Scalar("I32");
    public static SandboxType I64 { get; } = Scalar("I64");
    public static SandboxType F64 { get; } = Scalar("F64");
    public static SandboxType String { get; } = Scalar("String");
    public static SandboxType SandboxPath { get; } = Scalar("SandboxPath");
    public static SandboxType SandboxUri { get; } = Scalar("SandboxUri");
    public static SandboxType PlayerId { get; } = Scalar("PlayerId");
    public static SandboxType ItemId { get; } = Scalar("ItemId");
    public static SandboxType QuestId { get; } = Scalar("QuestId");
    public static SandboxType MapId { get; } = Scalar("MapId");

    public static SandboxType Scalar(string name) => new(name, []);

    public static SandboxType List(SandboxType item) => new("List", [item]);

    public static SandboxType Map(SandboxType key, SandboxType value) => new("Map", [key, value]);

    public static bool IsForbiddenName(string name)
        => ForbiddenNames.Contains(name) ||
           name.StartsWith("System.", StringComparison.Ordinal) ||
           name.StartsWith("Microsoft.", StringComparison.Ordinal);

    public static bool IsKnownOpaqueId(string name)
        => name is "PlayerId" or "ItemId" or "QuestId" or "MapId";

    public bool IsKnown(int maxDepth = 8) => IsKnown(this, 0, maxDepth);

    public bool IsForbidden() => IsForbidden(this);

    public bool IsValidMapKey()
        => Arguments.Count == 0 && MapKeyScalars.Contains(Name);

    public bool Equals(SandboxType? other)
        => other is not null &&
           StringComparer.Ordinal.Equals(Name, other.Name) &&
           Arguments.SequenceEqual(other.Arguments);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name, StringComparer.Ordinal);
        foreach (var argument in Arguments)
        {
            hash.Add(argument);
        }

        return hash.ToHashCode();
    }

    public override string ToString()
    {
        if (Arguments.Count == 0)
        {
            return Name;
        }

        var builder = new StringBuilder(Name);
        builder.Append('<');
        builder.Append(string.Join(",", Arguments.Select(a => a.ToString())));
        builder.Append('>');
        return builder.ToString();
    }

    private static bool IsKnown(SandboxType type, int depth, int maxDepth)
    {
        if (depth > maxDepth || IsForbiddenName(type.Name))
        {
            return false;
        }

        if (AllowedScalars.Contains(type.Name))
        {
            return type.Arguments.Count == 0;
        }

        return type.Name switch
        {
            "List" => type.Arguments.Count == 1 && type.Arguments.All(a => IsKnown(a, depth + 1, maxDepth)),
            "Map" => type.Arguments.Count == 2 &&
                type.Arguments[0].IsValidMapKey() &&
                type.Arguments.All(a => IsKnown(a, depth + 1, maxDepth)),
            _ => false
        };
    }

    private static bool IsForbidden(SandboxType type)
        => IsForbiddenName(type.Name) || type.Arguments.Any(IsForbidden);
}

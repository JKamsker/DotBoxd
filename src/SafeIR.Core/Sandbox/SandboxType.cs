using System.Text;

namespace SafeIR;

public sealed record SandboxType(string Name, IReadOnlyList<SandboxType> Arguments)
{
    private IReadOnlyList<SandboxType> _arguments = ModelCopy.List(Arguments);

    public IReadOnlyList<SandboxType> Arguments { get => _arguments; init => _arguments = ModelCopy.List(value); }

    private const int MaxOpaqueIdNameLength = 64;

    private static readonly HashSet<string> AllowedScalars = new(StringComparer.Ordinal) {
        "Unit", "Bool", "I32", "I64", "F64", "String",
        "SandboxPath", "SandboxUri"
    };

    private static readonly HashSet<string> MapKeyScalars = new(StringComparer.Ordinal) {
        "Bool", "I32", "I64", "String", "SandboxPath", "SandboxUri"
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

    public static SandboxType Scalar(string name) => new(name, []);

    public static SandboxType List(SandboxType item) => new("List", [item]);

    public static SandboxType Map(SandboxType key, SandboxType value) => new("Map", [key, value]);

    public static bool IsForbiddenName(string name)
        => ForbiddenNames.Contains(name) ||
           name.StartsWith("System.", StringComparison.Ordinal) ||
           name.StartsWith("Microsoft.", StringComparison.Ordinal);

    /// <summary>
    /// Structural predicate: a name denotes an opaque-id brand when it is a well-formed
    /// identifier that is not a built-in scalar, not a collection constructor, and not a
    /// forbidden CLR-shaped name. Whether a given brand is permitted for a particular run
    /// is a host/policy decision (see <c>SandboxPolicy.DeclaredOpaqueIdTypes</c>), not a
    /// structural one.
    /// </summary>
    public static bool IsKnownOpaqueId(string name)
        => IsWellFormedOpaqueIdName(name);

    public static bool IsWellFormedOpaqueIdName(string name)
    {
        if (string.IsNullOrEmpty(name) ||
            name.Length > MaxOpaqueIdNameLength ||
            AllowedScalars.Contains(name) ||
            name is "List" or "Map" ||
            IsForbiddenName(name) ||
            !char.IsAsciiLetterUpper(name[0]))
        {
            return false;
        }

        for (var i = 1; i < name.Length; i++)
        {
            var character = name[i];
            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    // Open structural check: any well-formed opaque-id brand is accepted. Used by runtime value
    // validation, which sees only types from an already-policy-validated module.
    public bool IsKnown(int maxDepth = 8) => IsKnown(this, 0, maxDepth, declaredOpaqueIdTypes: null);

    // Host-gated structural check: an opaque-id brand is accepted only when the host declared it.
    // Used by module validation (declaredOpaqueIdTypes from the policy) and, with an empty set, by
    // the binding registry (built-in scalars only).
    public bool IsKnown(IReadOnlySet<string> declaredOpaqueIdTypes, int maxDepth = 8)
        => IsKnown(this, 0, maxDepth, declaredOpaqueIdTypes);

    // Strict structural check: built-in scalars and collections only, no opaque-id brands.
    public bool IsKnownBuiltIn(int maxDepth = 8) => IsKnown(this, 0, maxDepth, EmptyOpaqueIdTypes);

    public bool IsForbidden() => IsForbidden(this);

    public bool IsValidMapKey() => IsValidMapKey(declaredOpaqueIdTypes: null);

    public bool IsValidMapKey(IReadOnlySet<string>? declaredOpaqueIdTypes)
        => Arguments.Count == 0 &&
           (MapKeyScalars.Contains(Name) || IsAcceptedOpaqueIdBrand(Name, declaredOpaqueIdTypes));

    private static readonly IReadOnlySet<string> EmptyOpaqueIdTypes =
        new HashSet<string>(StringComparer.Ordinal);

    private static bool IsAcceptedOpaqueIdBrand(string name, IReadOnlySet<string>? declaredOpaqueIdTypes)
        => IsWellFormedOpaqueIdName(name) &&
           (declaredOpaqueIdTypes is null || declaredOpaqueIdTypes.Contains(name));

    public bool Equals(SandboxType? other)
    {
        if (other is null ||
            !StringComparer.Ordinal.Equals(Name, other.Name) ||
            Arguments.Count != other.Arguments.Count)
        {
            return false;
        }

        for (var i = 0; i < Arguments.Count; i++)
        {
            if (!Arguments[i].Equals(other.Arguments[i]))
            {
                return false;
            }
        }

        return true;
    }

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
        for (var i = 0; i < Arguments.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Arguments[i].ToString());
        }

        builder.Append('>');
        return builder.ToString();
    }

    private static bool IsKnown(SandboxType type, int depth, int maxDepth, IReadOnlySet<string>? declaredOpaqueIdTypes)
    {
        if (depth > maxDepth || IsForbiddenName(type.Name))
        {
            return false;
        }

        if (type.Arguments.Count == 0)
        {
            return AllowedScalars.Contains(type.Name) ||
                   IsAcceptedOpaqueIdBrand(type.Name, declaredOpaqueIdTypes);
        }

        if (type.Name == "List")
        {
            return type.Arguments.Count == 1 &&
                   IsKnown(type.Arguments[0], depth + 1, maxDepth, declaredOpaqueIdTypes);
        }

        return type.Name == "Map" &&
               type.Arguments.Count == 2 &&
               type.Arguments[0].IsValidMapKey(declaredOpaqueIdTypes) &&
               IsKnown(type.Arguments[0], depth + 1, maxDepth, declaredOpaqueIdTypes) &&
               IsKnown(type.Arguments[1], depth + 1, maxDepth, declaredOpaqueIdTypes);
    }

    private static bool IsForbidden(SandboxType type)
    {
        if (IsForbiddenName(type.Name))
        {
            return true;
        }

        for (var i = 0; i < type.Arguments.Count; i++)
        {
            if (IsForbidden(type.Arguments[i]))
            {
                return true;
            }
        }

        return false;
    }
}

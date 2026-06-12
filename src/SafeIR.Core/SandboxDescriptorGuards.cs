namespace SafeIR;

public static class SandboxDescriptorGuards
{
    private static readonly string[] ForbiddenFragments = [
        "System.", "Microsoft.", "Assembly.", "Type.", "Reflection.", "Process.",
        "Environment.", "Thread.", "Task.", "DllImport", "IServiceProvider",
        "RuntimeMethodHandle", "RuntimeTypeHandle", "RuntimeFieldHandle",
        "MetadataToken", "AssemblyQualifiedName", "PublicKeyToken=", "Culture=",
        "assemblyPath", "rawDll", "rawIl", "rawMsil", "plugin.dll",
        "clrType", "hostCode", "loader", "msil", ".dll"
    ];

    private static readonly string[] ForbiddenIlFragments = [
        "IL_", "ldtoken", "ldftn", "ldvirtftn", "calli"
    ];

    private static readonly string[] MetadataTokenPrefixes = [
        "0x02", "0x04", "0x06", "0x0a", "0x1b", "0x23", "0x70"
    ];

    public static bool ContainsForbiddenDescriptor(string value)
        => ForbiddenFragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase)) ||
           ForbiddenIlFragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase)) ||
           LooksLikeAssemblyQualifiedName(value) ||
           ContainsMetadataToken(value);

    private static bool LooksLikeAssemblyQualifiedName(string value)
        => value.Contains(", Version=", StringComparison.OrdinalIgnoreCase) ||
           value.Contains(", PublicKeyToken=", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsMetadataToken(string value)
    {
        for (var index = 0; index <= value.Length - 8; index++)
        {
            if (IsMetadataTokenPrefix(value, index) &&
                Enumerable.Range(index + 2, 6).All(i => IsHex(value[i])) &&
                IsTokenBoundary(value, index - 1) &&
                IsTokenBoundary(value, index + 8))
            {
                return true;
            }
        }

        for (var index = 0; index <= value.Length - 10; index++)
        {
            var candidate = value.Substring(index, 4);
            if (!MetadataTokenPrefixes.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Enumerable.Range(index + 4, 6).All(i => IsHex(value[i])))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMetadataTokenPrefix(string value, int index)
    {
        if (index < 0 || index + 1 >= value.Length)
        {
            return false;
        }

        var prefix = value.Substring(index, 2);
        return MetadataTokenPrefixes.Any(token =>
            token.AsSpan(2).Equals(prefix.AsSpan(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTokenBoundary(string value, int index)
        => index < 0 || index >= value.Length || !IsHex(value[index]);

    private static bool IsHex(char value)
        => value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}

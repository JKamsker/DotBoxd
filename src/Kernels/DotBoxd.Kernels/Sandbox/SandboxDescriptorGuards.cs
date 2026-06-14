namespace DotBoxd.Kernels;

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
        => ContainsAnyFragment(value, ForbiddenFragments) ||
           ContainsAnyFragment(value, ForbiddenIlFragments) ||
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
                IsHexRun(value, index + 2, 6) &&
                IsTokenBoundary(value, index - 1) &&
                IsTokenBoundary(value, index + 8))
            {
                return true;
            }
        }

        for (var index = 0; index <= value.Length - 10; index++)
        {
            if (!IsPrefixedMetadataToken(value.AsSpan(index, 4)))
            {
                continue;
            }

            if (IsHexRun(value, index + 4, 6) &&
                IsTokenBoundary(value, index - 1) &&
                IsTokenBoundary(value, index + 10))
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

        var prefix = value.AsSpan(index, 2);
        for (var i = 0; i < MetadataTokenPrefixes.Length; i++)
        {
            if (MetadataTokenPrefixes[i].AsSpan(2).Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAnyFragment(string value, string[] fragments)
    {
        for (var i = 0; i < fragments.Length; i++)
        {
            if (value.Contains(fragments[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPrefixedMetadataToken(ReadOnlySpan<char> candidate)
    {
        for (var i = 0; i < MetadataTokenPrefixes.Length; i++)
        {
            if (MetadataTokenPrefixes[i].AsSpan().Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHexRun(string value, int start, int length)
    {
        for (var i = start; i < start + length; i++)
        {
            if (!IsHex(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTokenBoundary(string value, int index)
        => index < 0 || index >= value.Length || !IsHex(value[index]);

    private static bool IsHex(char value)
        => value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}

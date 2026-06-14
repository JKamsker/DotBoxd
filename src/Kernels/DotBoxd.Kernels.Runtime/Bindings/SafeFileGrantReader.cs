namespace DotBoxd.Kernels.Runtime;

using System.Globalization;
using System.Runtime.CompilerServices;
using DotBoxd.Kernels;

// File capability grants are stable for the lifetime of a CapabilityGrant instance,
// so the byte limit, create/overwrite flags, and allowed-extension set are decoded
// once per grant and cached. Runtime file bindings reuse the typed options instead of
// reparsing raw CapabilityGrant.Parameters strings (and re-splitting the extension CSV)
// on every file.readText / file.writeText call. Invalid parameters still fail closed
// with the same PermissionDenied error on first decode. Mirrors the grant-reader
// caching pattern used by the transport addon's HTTP grant reader.
internal static class SafeFileGrantReader
{
    private static readonly ConditionalWeakTable<CapabilityGrant, SafeFileGrantOptions> Cache = new();

    public static SafeFileGrantOptions Read(CapabilityGrant grant)
        => Cache.GetValue(grant, CreateOptions);

    private static SafeFileGrantOptions CreateOptions(CapabilityGrant grant)
        => new(
            ReadOptionalLong(grant, "maxBytesPerRun"),
            ReadBool(grant, "allowCreate"),
            ReadBool(grant, "allowOverwrite"),
            ReadExtensions(grant));

    private static IReadOnlySet<string>? ReadExtensions(CapabilityGrant grant)
    {
        if (!grant.Parameters.TryGetValue("allowedExtensions", out var allowed) || string.IsNullOrWhiteSpace(allowed))
        {
            return null;
        }

        return allowed
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool ReadBool(CapabilityGrant grant, string key)
    {
        if (!grant.Parameters.TryGetValue(key, out var value))
        {
            return false;
        }

        if (!bool.TryParse(value, out var parsed))
        {
            throw Error(key);
        }

        return parsed;
    }

    private static long? ReadOptionalLong(CapabilityGrant grant, string key)
    {
        if (!grant.Parameters.TryGetValue(key, out var value))
        {
            return null;
        }

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            throw Error(key);
        }

        return parsed;
    }

    private static SandboxRuntimeException Error(string key)
        => new(new SandboxError(SandboxErrorCode.PermissionDenied, $"file grant denied: parameter '{key}' is invalid"));
}

internal sealed record SafeFileGrantOptions(
    long? MaxBytesPerRun,
    bool AllowCreate,
    bool AllowOverwrite,
    IReadOnlySet<string>? AllowedExtensions);

namespace DotBoxD.Kernels.Verifier;

using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;

internal static class AssemblyReferenceIdentity
{
    public static string Format(MetadataReader reader, AssemblyReference reference)
    {
        var name = reader.GetString(reference.Name);
        var culture = reference.Culture.IsNil ? "neutral" : reader.GetString(reference.Culture);
        var token = reference.PublicKeyOrToken.IsNil
            ? "null"
            : PublicKeyToken(reader.GetBlobBytes(reference.PublicKeyOrToken), reference.Flags);
        return $"{name}, Version={reference.Version}, Culture={culture}, PublicKeyToken={token}";
    }

    private static string PublicKeyToken(byte[] keyOrToken, AssemblyFlags flags)
    {
        if ((flags & AssemblyFlags.PublicKey) == 0)
        {
            return Convert.ToHexString(keyOrToken).ToLowerInvariant();
        }

        var hash = SHA1.HashData(keyOrToken);
        Array.Reverse(hash, hash.Length - 8, 8);
        return Convert.ToHexString(hash.AsSpan(hash.Length - 8)).ToLowerInvariant();
    }
}

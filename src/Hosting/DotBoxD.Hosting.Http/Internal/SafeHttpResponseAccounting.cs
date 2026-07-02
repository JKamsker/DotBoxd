using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Http.Internal;

using System.Net.Http.Headers;
using System.Text;

internal static class SafeHttpResponseAccounting
{
    private const int HttpPrefixBytes = 5; // "HTTP/"
    private const int SpaceBytes = 1;
    private const int HeaderSeparatorBytes = 2; // ": "
    private const int LineEndingBytes = 2; // "\r\n"

    public static long ChargeMetadata(
        SandboxContext context,
        HttpResponseMessage response,
        long maxBytes)
    {
        var bytes = MeasureMetadataBytes(response);
        context.Budget.ChargeNetworkRead(bytes);
        if (bytes > maxBytes)
        {
            throw QuotaExceeded();
        }

        return bytes;
    }

    public static long MeasureMetadataBytes(HttpResponseMessage response)
    {
        var reason = response.ReasonPhrase ?? string.Empty;
        var bytes = CheckedAdd(0, HttpPrefixBytes);
        bytes = CheckedAdd(bytes, VersionByteCount(response.Version));
        bytes = CheckedAdd(bytes, SpaceBytes);
        bytes = CheckedAdd(bytes, Int32AsciiByteCount((int)response.StatusCode));
        bytes = CheckedAdd(bytes, SpaceBytes);
        bytes = CheckedAdd(bytes, Encoding.UTF8.GetByteCount(reason));
        bytes = CheckedAdd(bytes, LineEndingBytes);
        bytes = CheckedAdd(bytes, HeaderBytes(response.Headers));
        bytes = CheckedAdd(bytes, HeaderBytes(response.Content.Headers));
        return CheckedAdd(bytes, LineEndingBytes);
    }

    private static long HeaderBytes(HttpHeaders headers)
    {
        long bytes = 0;
        foreach (var header in headers.NonValidated)
        {
            var keyBytes = Encoding.UTF8.GetByteCount(header.Key);
            foreach (var value in header.Value)
            {
                bytes = CheckedAdd(bytes, keyBytes);
                bytes = CheckedAdd(bytes, HeaderSeparatorBytes);
                bytes = CheckedAdd(bytes, Encoding.UTF8.GetByteCount(value));
                bytes = CheckedAdd(bytes, LineEndingBytes);
            }
        }

        return bytes;
    }

    private static int VersionByteCount(Version version)
    {
        var bytes = NonNegativeInt32AsciiByteCount(version.Major) + 1 + NonNegativeInt32AsciiByteCount(version.Minor);
        if (version.Build >= 0)
        {
            bytes += 1 + NonNegativeInt32AsciiByteCount(version.Build);
            if (version.Revision >= 0)
            {
                bytes += 1 + NonNegativeInt32AsciiByteCount(version.Revision);
            }
        }

        return bytes;
    }

    private static int Int32AsciiByteCount(int value)
        => value < 0
            ? value == int.MinValue ? 11 : 1 + NonNegativeInt32AsciiByteCount(-value)
            : NonNegativeInt32AsciiByteCount(value);

    private static int NonNegativeInt32AsciiByteCount(int value)
    {
        var digits = 1;
        while (value >= 10)
        {
            value /= 10;
            digits++;
        }

        return digits;
    }

    private static long CheckedAdd(long left, long right)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            throw QuotaExceeded();
        }
    }

    private static SandboxRuntimeException QuotaExceeded()
        => new(new SandboxError(
            SandboxErrorCode.QuotaExceeded,
            "net.http.get denied: response exceeds byte limit"));
}

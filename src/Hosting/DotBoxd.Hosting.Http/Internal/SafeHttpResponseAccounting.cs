namespace DotBoxd.Hosting.Http.Internal;

using System.Net.Http.Headers;
using System.Text;
using DotBoxd.Kernels;

internal static class SafeHttpResponseAccounting
{
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
        var bytes = CheckedAdd(
            0,
            Encoding.UTF8.GetByteCount($"HTTP/{response.Version} {(int)response.StatusCode} {reason}\r\n"));
        bytes = CheckedAdd(bytes, HeaderBytes(response.Headers));
        bytes = CheckedAdd(bytes, HeaderBytes(response.Content.Headers));
        return CheckedAdd(bytes, 2);
    }

    private static long HeaderBytes(HttpHeaders headers)
    {
        long bytes = 0;
        foreach (var header in headers)
        {
            foreach (var value in header.Value)
            {
                bytes = CheckedAdd(bytes, Encoding.UTF8.GetByteCount(header.Key));
                bytes = CheckedAdd(bytes, Encoding.UTF8.GetByteCount(": "));
                bytes = CheckedAdd(bytes, Encoding.UTF8.GetByteCount(value));
                bytes = CheckedAdd(bytes, Encoding.UTF8.GetByteCount("\r\n"));
            }
        }

        return bytes;
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

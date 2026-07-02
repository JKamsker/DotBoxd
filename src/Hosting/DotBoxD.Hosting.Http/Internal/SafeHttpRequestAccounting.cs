using System.Text;

namespace DotBoxD.Hosting.Http.Internal;

internal static class SafeHttpRequestAccounting
{
    private const string RequestLinePrefix = "GET ";
    private const string RequestLineSuffixAndHostPrefix = " HTTP/1.1\r\nHost: ";
    private const string HeaderTerminator = "\r\n\r\n";

    public static long MeasureGetRequestBytes(Uri uri)
        => RequestLinePrefix.Length +
           Encoding.UTF8.GetByteCount(uri.PathAndQuery) +
           RequestLineSuffixAndHostPrefix.Length +
           Encoding.UTF8.GetByteCount(HostHeader(uri)) +
           HeaderTerminator.Length;

    private static string HostHeader(Uri uri)
        => uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
}
